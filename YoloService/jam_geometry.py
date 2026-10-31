"""Spatial traffic evidence from boxes; no speed or metric road density is inferred."""

from dataclasses import asdict, dataclass
import math

import cv2
import numpy as np


VEHICLE_NAMES = {2: "car", 3: "motorcycle", 5: "bus", 7: "truck"}


@dataclass(frozen=True)
class GeometryThresholds:
    # Experimental operating points, not traffic engineering standards.
    min_vehicles: int = 4
    min_occupied_ratio: float = 0.35
    min_cluster_ratio: float = 0.60
    min_cluster_size: int = 3
    max_normalized_gap: float = 0.25
    max_scale_ratio: float = 2.5

    def __post_init__(self):
        if self.min_vehicles < 2 or not 2 <= self.min_cluster_size <= self.min_vehicles:
            raise ValueError("Require 2 <= min_cluster_size <= min_vehicles")
        for name in ("min_occupied_ratio", "min_cluster_ratio"):
            if not 0 < getattr(self, name) <= 1:
                raise ValueError(f"{name} must be in (0, 1]")
        if not math.isfinite(self.max_normalized_gap) or self.max_normalized_gap < 0:
            raise ValueError("max_normalized_gap must be finite and nonnegative")
        if not math.isfinite(self.max_scale_ratio) or self.max_scale_ratio < 1:
            raise ValueError("max_scale_ratio must be finite and >= 1")


def validate_roi(points):
    """A normalized convex polygon, supplied for this image, not guessed from boxes."""
    if points is None:
        return None
    if not 3 <= len(points) <= 12:
        raise ValueError("jam_roi needs 3 to 12 normalized polygon points")
    polygon = np.asarray(points, dtype=np.float64)
    if polygon.shape != (len(points), 2) or not np.isfinite(polygon).all():
        raise ValueError("jam_roi must contain finite [x, y] points")
    if (polygon < 0).any() or (polygon > 1).any():
        raise ValueError("jam_roi coordinates must be between 0 and 1")
    if len(np.unique(polygon, axis=0)) != len(polygon):
        raise ValueError("jam_roi contains duplicate points")
    contour = polygon.astype(np.float32)
    if not cv2.isContourConvex(contour) or cv2.contourArea(contour) < 0.001:
        raise ValueError("jam_roi must be a nondegenerate convex polygon in boundary order")
    return polygon


def assess_geometry(boxes_xyxy, class_ids, image_size, roi=None, thresholds=None):
    """Return observable geometry, a rule score and a provisional state.

    Occupancy is the union of projected BOXES within the region, not actual road
    surface occupancy. Gaps are normalized by box diagonals, not measured in m.
    Similar-size neighbors reduce, but do not correct, perspective distortion.
    """
    thresholds = thresholds or GeometryThresholds()
    width, height = image_size
    if width <= 0 or height <= 0:
        raise ValueError("Image dimensions must be positive")
    if len(boxes_xyxy) != len(class_ids):
        raise ValueError("Box and class counts do not match")
    polygon = validate_roi(roi)
    contour = None if polygon is None else (polygon * [width, height]).astype(np.float32)
    counts = dict.fromkeys(VEHICLE_NAMES.values(), 0)
    total_detected = 0
    selected = []
    for coordinates, class_id in zip(boxes_xyxy, class_ids):
        name = VEHICLE_NAMES.get(int(class_id))
        if name is None:
            continue
        box = np.asarray(coordinates, dtype=np.float64)
        if box.shape != (4,) or not np.isfinite(box).all():
            continue
        x1, x2 = np.clip([box[0], box[2]], 0, width)
        y1, y2 = np.clip([box[1], box[3]], 0, height)
        if x2 <= x1 or y2 <= y1:
            continue
        total_detected += 1
        # Bottom-center approximates the vehicle's ground contact. No lane or
        # travel direction is inferred; callers must select the relevant road.
        if contour is not None and cv2.pointPolygonTest(contour, (float((x1 + x2) / 2), float(y2)), False) < 0:
            continue
        selected.append((x1, y1, x2, y2))
        counts[name] += 1

    # A bounded raster avoids double-counting overlapping boxes and allows
    # clipping to a polygon without adding a heavyweight geometry dependency.
    scale = 384 / max(width, height)
    mask_width = max(1, round(width * scale))
    mask_height = max(1, round(height * scale))
    region = np.ones((mask_height, mask_width), dtype=np.uint8)
    if polygon is not None:
        region.fill(0)
        pixels = np.rint(polygon * [mask_width - 1, mask_height - 1]).astype(np.int32)
        cv2.fillPoly(region, [pixels], 1)
    occupied = np.zeros_like(region)
    for x1, y1, x2, y2 in selected:
        left = max(0, int(math.floor(x1 / width * mask_width)))
        right = min(mask_width, int(math.ceil(x2 / width * mask_width)))
        top = max(0, int(math.floor(y1 / height * mask_height)))
        bottom = min(mask_height, int(math.ceil(y2 / height * mask_height)))
        occupied[top:bottom, left:right] = 1
    occupancy = float(np.count_nonzero(occupied & region) / max(1, np.count_nonzero(region)))

    n = len(selected)
    neighbors = [set() for _ in selected]
    nearest_gaps = [math.inf] * n
    for i in range(n):
        ax1, ay1, ax2, ay2 = selected[i]
        a_diagonal = math.hypot(ax2 - ax1, ay2 - ay1)
        for j in range(i + 1, n):
            bx1, by1, bx2, by2 = selected[j]
            b_diagonal = math.hypot(bx2 - bx1, by2 - by1)
            if max(a_diagonal, b_diagonal) / min(a_diagonal, b_diagonal) > thresholds.max_scale_ratio:
                continue
            dx = max(ax1 - bx2, bx1 - ax2, 0)
            dy = max(ay1 - by2, by1 - ay2, 0)
            gap = math.hypot(dx, dy) / ((a_diagonal + b_diagonal) / 2)
            nearest_gaps[i] = min(nearest_gaps[i], gap)
            nearest_gaps[j] = min(nearest_gaps[j], gap)
            if gap <= thresholds.max_normalized_gap:
                neighbors[i].add(j)
                neighbors[j].add(i)

    visited = set()
    largest_cluster = 0
    for i in range(n):
        if i in visited:
            continue
        stack, size = [i], 0
        visited.add(i)
        while stack:
            node = stack.pop()
            size += 1
            for other in neighbors[node] - visited:
                visited.add(other)
                stack.append(other)
        largest_cluster = max(largest_cluster, size)

    cluster_ratio = largest_cluster / n if n else 0.0
    enough_vehicles = n >= thresholds.min_vehicles
    enough_occupancy = occupancy >= thresholds.min_occupied_ratio
    compact_cluster = largest_cluster >= thresholds.min_cluster_size and cluster_ratio >= thresholds.min_cluster_ratio
    suspected = enough_vehicles and enough_occupancy and compact_cluster
    finite_gaps = [gap for gap in nearest_gaps if math.isfinite(gap)]
    score = (
        min(n / thresholds.min_vehicles, 1)
        * min(occupancy / thresholds.min_occupied_ratio, 1)
        * min(cluster_ratio / thresholds.min_cluster_ratio, 1)
        * min(largest_cluster / thresholds.min_cluster_size, 1)
    )
    metrics = {
        "method": "image_geometry_v1",
        "region_source": "provided_roi" if polygon is not None else "full_image",
        "roi": None if polygon is None else polygon.tolist(),
        "detected_vehicle_count": total_detected,
        "vehicle_count": n,
        "excluded_vehicle_count": total_detected - n,
        "box_occupancy_ratio": round(occupancy, 4),
        "close_neighbor_ratio": round(sum(bool(v) for v in neighbors) / n, 4) if n else 0.0,
        "largest_cluster_size": largest_cluster,
        "largest_cluster_ratio": round(cluster_ratio, 4),
        "median_normalized_gap": round(float(np.median(finite_gaps)), 4) if finite_gaps else None,
        "heuristic_score": round(score, 4),
        "score_is_probability": False,
        "criteria": {
            "enough_vehicles": enough_vehicles,
            "enough_occupancy": enough_occupancy,
            "compact_cluster": compact_cluster,
        },
        "thresholds": asdict(thresholds),
        "limitations": [
            "Projected box area is not calibrated road density.",
            "Normalized gaps are not distances in meters.",
            "No speed, travel direction, parked-vehicle or duration inference.",
            "Operating thresholds require evaluation on representative labeled images.",
        ],
    }
    if polygon is None:
        metrics["limitations"].append("No road ROI supplied; the whole image is used.")
    scope = "vùng đường được cung cấp" if polygon is not None else "toàn ảnh (chưa xác định vùng đường)"
    result = "Nghi ngờ xe dồn sát, cần xác minh" if suspected else "Chưa đủ dấu hiệu không gian để cảnh báo ùn tắc"
    evidence = (
        f"Trong {scope}: {n} xe; khung xe chiếm {occupancy:.1%} vùng xét; "
        f"cụm xe gần nhau lớn nhất {largest_cluster}/{n} xe. {result}. "
        "Đây là ước lượng trên ảnh, không đo tốc độ hoặc khoảng cách thực tế."
    )
    return {
        "status": "suspected" if suspected else "no_evidence",
        "counts_by_type": counts,
        "metrics": metrics,
        "evidence": evidence,
    }

"""
YOLO Dual-Model FastAPI Microservice
=====================================
Serves two YOLO models simultaneously:
  - Model 1 (accident-fire): CLASSIFICATION → exact classes: accident, fire, reject
  - Model 2 (traffic-jam):   CLASSIFICATION (jam/normal)
                              HOẶC DETECTION (COCO) → bằng chứng không gian từ khung xe

Endpoints:
  POST /api/yolo/classify   - Classify image (URL or base64)
  GET  /api/yolo/health     - Health check & status
"""

import asyncio
import io
import logging
import os
import time
from contextlib import asynccontextmanager
from typing import Literal, Optional

import httpx
import uvicorn
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field, field_validator

if __package__:
    from .jam_geometry import GeometryThresholds, assess_geometry, validate_roi
else:
    from jam_geometry import GeometryThresholds, assess_geometry, validate_roi

# ---------------------------------------------------------------------------
# Logging
# ---------------------------------------------------------------------------
logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("yolo-service")

# ---------------------------------------------------------------------------
# Strict Label Sets
# ---------------------------------------------------------------------------
# Model tai nạn/hỏa hoạn BẮT BUỘC phải có đầy đủ và chính xác 3 nhãn này
EXPECTED_ACCIDENT_FIRE_CLASSES = {"accident", "fire", "reject"}

# ---------------------------------------------------------------------------
# Config – Model paths (Ưu tiên thư mục nội bộ YoloService/models)
# ---------------------------------------------------------------------------
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
MODELS_DIR = os.path.join(BASE_DIR, "models")

MODEL_ACCIDENT_FIRE_PATH = os.getenv(
    "YOLO_ACCIDENT_FIRE_MODEL",
    # Ưu tiên 1: File nội bộ ngay trong project backend
    os.path.join(MODELS_DIR, "accident_fire_best.pt"),
)

MODEL_ACCIDENT_FIRE_FALLBACK = os.getenv(
    "YOLO_ACCIDENT_FIRE_FALLBACK",
    r"C:\Users\vohot\Downloads\yolo11_t4x2_vwihptyp_results\training\yolo11n_cls_t4x2\weights\best.pt",
)

MODEL_JAM_PATH = os.getenv(
    "YOLO_JAM_MODEL",
    # Ưu tiên 1: File nội bộ ngay trong project backend
    os.path.join(MODELS_DIR, "traffic_jam.pt"),
)

# Ngưỡng thử nghiệm cho bằng chứng không gian, không phải tiêu chuẩn giao thông.
JAM_GEOMETRY_THRESHOLDS = GeometryThresholds(
    min_vehicles=int(os.getenv("JAM_MIN_VEHICLES", "4")),
    min_occupied_ratio=float(os.getenv("JAM_MIN_BOX_OCCUPANCY", "0.35")),
    min_cluster_ratio=float(os.getenv("JAM_MIN_CLUSTER_RATIO", "0.60")),
    min_cluster_size=int(os.getenv("JAM_MIN_CLUSTER_SIZE", "3")),
    max_normalized_gap=float(os.getenv("JAM_MAX_NORMALIZED_GAP", "0.25")),
    max_scale_ratio=float(os.getenv("JAM_MAX_SCALE_RATIO", "2.5")),
)

# ---------------------------------------------------------------------------
# Pydantic Schemas
# ---------------------------------------------------------------------------

class ClassifyRequest(BaseModel):
    """Request body for /api/yolo/classify"""
    image_url: Optional[str] = Field(None, description="Public URL of the image to classify")
    image_base64: Optional[str] = Field(None, description="Base64-encoded image data")
    jam_roi: Optional[list[tuple[float, float]]] = Field(
        None,
        description="Optional convex road polygon for THIS image, 3-12 [x,y] points normalized to [0,1]. Applies to detection only; never inferred automatically.",
    )

    @field_validator("jam_roi")
    @classmethod
    def check_jam_roi(cls, value):
        validate_roi(value)
        return value


class JamGeometryMetrics(BaseModel):
    method: str
    region_source: Literal["provided_roi", "full_image"]
    roi: Optional[list[tuple[float, float]]] = None
    detected_vehicle_count: int
    vehicle_count: int
    excluded_vehicle_count: int
    box_occupancy_ratio: float
    close_neighbor_ratio: float
    largest_cluster_size: int
    largest_cluster_ratio: float
    median_normalized_gap: Optional[float] = None
    heuristic_score: float
    score_is_probability: bool = False
    criteria: dict[str, bool]
    thresholds: dict[str, float | int]
    limitations: list[str]


class YoloModelResult(BaseModel):
    """Result from a single YOLO model"""
    model_name: str
    model_task: str = "unknown"          # "classify" hoặc "detect"
    predicted_class: str
    confidence: Optional[float] = Field(..., description="Classifier score; null for geometry heuristics (not a calibrated jam probability).")
    all_probabilities: dict[str, float] = Field(default_factory=dict)
    counts_by_type: dict[str, int] = Field(default_factory=dict)
    traffic_jam_status: str = "no_evidence"  # "suspected" | "no_evidence" | "unverified"
    traffic_jam_evidence: Optional[str] = None
    geometry_metrics: Optional[JamGeometryMetrics] = None
    inference_ms: float = 0.0
    is_available: bool = True
    error: Optional[str] = None


class ClassifyResponse(BaseModel):
    """
    Merged response với phân định trạng thái rõ ràng từng mô hình.
    Tránh nhầm lẫn giữa 'không đánh giá được' và 'đã kiểm tra an toàn'.
    """
    is_error: bool = False
    error_message: Optional[str] = None

    # Trạng thái đánh giá riêng biệt
    is_accident_fire_evaluated: bool = False
    is_jam_evaluated: bool = False

    # Loại sự cố: "fire", "accident", "none", hoặc "unverified" khi model lỗi
    incident_type: str = "unverified"

    # Confidence riêng cho incident type (chỉ có giá trị khi model tai nạn/cháy chạy thành công)
    incident_confidence: Optional[float] = None

    # Đánh giá ùn tắc giao thông (Mô hình trạng thái nhiều mức)
    traffic_jam_status: str = "no_evidence"  # "suspected" | "no_evidence" | "unverified"
    traffic_jam_evidence: Optional[str] = None
    is_traffic_jam: bool = False             # Chỉ True khi đã được xác nhận, AI dừng ở mức Suspected
    jam_confidence: Optional[float] = Field(None, description="Classifier score only. Geometry heuristics return null; inspect jam_geometry instead.")
    is_jam_heuristic: bool = False           # True nếu dùng heuristic không gian thay vì classifier jam
    jam_vehicle_count: Optional[int] = None
    jam_counts_by_type: dict[str, int] = Field(default_factory=dict)
    jam_geometry: Optional[JamGeometryMetrics] = None

    # Severity: YOLO KHÔNG tự định nghĩa severity thực tế (mức 1-3).
    # Luôn trả 0 (Unclassified), cờ needs_human_review = True khi cần thẩm định.
    severity_score: int = 0
    needs_human_review: bool = False

    hazard_tags: list[str] = []
    summary: str = ""
    models: list[YoloModelResult] = []
    total_inference_ms: float = 0.0


class HealthResponse(BaseModel):
    status: str                         # "healthy" | "degraded" | "unhealthy"
    is_accident_fire_ready: bool
    is_jam_ready: bool
    models_loaded: dict[str, bool]
    models_info: dict[str, str]
    gpu_available: bool


# ---------------------------------------------------------------------------
# Global model holders + metadata
# ---------------------------------------------------------------------------
accident_fire_model = None
jam_model = None
device_info = "cpu"

af_model_task: str = "unknown"
af_model_names: dict = {}
jam_model_task: str = "unknown"
jam_model_names: dict = {}


def validate_accident_fire_model(model, model_name: str) -> bool:
    """
    Kiểm tra chặt chẽ:
    1. model.task BẮT BUỘC là 'classify'
    2. Tập nhãn BẮT BUỘC phải trùng khớp chính xác: {'accident', 'fire', 'reject'}
    """
    task = getattr(model, "task", None)
    names = getattr(model, "names", {})

    if task != "classify":
        logger.error(
            f"❌ {model_name}: Mong đợi task='classify' nhưng nhận được task='{task}'. "
            f"Model này KHÔNG phải classification."
        )
        return False

    actual_classes = {str(v).lower().strip() for v in names.values()}
    if actual_classes != EXPECTED_ACCIDENT_FIRE_CLASSES:
        logger.error(
            f"❌ {model_name}: Tập nhãn KHÔNG khớp chính xác yêu cầu. "
            f"Yêu cầu: {EXPECTED_ACCIDENT_FIRE_CLASSES}, Thực tế: {actual_classes}. "
            f"Model bị từ chối tải."
        )
        return False

    logger.info(f"✅ {model_name}: task='{task}', classes={actual_classes}")
    return True


def load_models():
    """Load YOLO models at startup với validation nghiêm ngặt"""
    global accident_fire_model, jam_model, device_info
    global af_model_task, af_model_names, jam_model_task, jam_model_names

    try:
        from ultralytics import YOLO
        import torch

        device_info = "cuda" if torch.cuda.is_available() else "cpu"
        logger.info(f"PyTorch device: {device_info}")

        # ─── 1. Load accident-fire model ───
        af_path = MODEL_ACCIDENT_FIRE_PATH
        if not os.path.exists(af_path):
            logger.info(f"Primary accident-fire model not found at: {af_path}")
            af_path = MODEL_ACCIDENT_FIRE_FALLBACK
            logger.info(f"Trying fallback: {af_path}")

        if os.path.exists(af_path):
            candidate = YOLO(af_path)
            if validate_accident_fire_model(candidate, "yolo11n-cls-accident-fire"):
                accident_fire_model = candidate
                af_model_task = getattr(candidate, "task", "unknown")
                af_model_names = getattr(candidate, "names", {})
            else:
                accident_fire_model = None
                logger.error(f"❌ File tại {af_path} không vượt qua kiểm tra nhãn. Model tai nạn/cháy KHÔNG được bật.")
        else:
            logger.warning(f"⚠️ Accident-Fire model NOT found at any path")

        # ─── 2. Load jam model ───
        if os.path.exists(MODEL_JAM_PATH):
            candidate = YOLO(MODEL_JAM_PATH)
            jam_model_task = getattr(candidate, "task", "unknown")
            jam_model_names = getattr(candidate, "names", {})

            if jam_model_task == "classify":
                actual_classes = {str(v).lower().strip() for v in jam_model_names.values()}
                if {"jam", "normal"}.issubset(actual_classes) or {"traffic_jam", "normal"}.issubset(actual_classes):
                    jam_model = candidate
                    logger.info(f"✅ Traffic Jam model loaded as CLASSIFICATION từ: {MODEL_JAM_PATH}")
                else:
                    logger.warning(f"⚠️ Jam model task='classify' nhưng nhãn {actual_classes} không hợp lệ.")
            elif jam_model_task == "detect":
                # Detection COCO chỉ cung cấp bằng chứng, không tự xác nhận jam.
                jam_model = candidate
                logger.info(
                    f"ℹ️ Traffic Jam model loaded as DETECTION từ: {MODEL_JAM_PATH}. "
                    "Chế độ: HEURISTIC không gian (mức chiếm chỗ và cụm xe gần nhau; không đo tốc độ/khoảng cách thực tế)."
                )
            else:
                logger.error(f"❌ Jam model task='{jam_model_task}' không được hỗ trợ.")
        else:
            logger.warning(f"⚠️ Traffic Jam model NOT found at: {MODEL_JAM_PATH}")

    except Exception as e:
        logger.error(f"❌ Failed to load models: {e}")


# ---------------------------------------------------------------------------
# App Lifecycle
# ---------------------------------------------------------------------------

@asynccontextmanager
async def lifespan(app: FastAPI):
    load_models()
    yield
    logger.info("Shutting down YOLO service")


app = FastAPI(
    title="YOLO Emergency Classification Service",
    version="2.2.0",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
async def download_image(url: str) -> bytes:
    async with httpx.AsyncClient(timeout=30.0) as client:
        resp = await client.get(url)
        resp.raise_for_status()
        return resp.content


def decode_base64_image(b64: str) -> bytes:
    import base64
    if "," in b64:
        b64 = b64.split(",", 1)[1]
    return base64.b64decode(b64)


# ---------------------------------------------------------------------------
# Inference Functions
# ---------------------------------------------------------------------------
def run_classification_inference(model, image_bytes: bytes, model_name: str) -> YoloModelResult:
    if model is None:
        return YoloModelResult(
            model_name=model_name,
            model_task="classify",
            predicted_class="unknown",
            confidence=0.0,
            inference_ms=0.0,
            is_available=False,
            error="Model not loaded or failed validation",
        )

    try:
        from PIL import Image

        start = time.perf_counter()
        img = Image.open(io.BytesIO(image_bytes)).convert("RGB")
        results = model(img, verbose=False)
        elapsed_ms = (time.perf_counter() - start) * 1000

        if not results or len(results) == 0:
            return YoloModelResult(
                model_name=model_name,
                model_task="classify",
                predicted_class="unknown",
                confidence=0.0,
                inference_ms=elapsed_ms,
                is_available=True,
                error="No results returned",
            )

        result = results[0]
        probs = result.probs

        if probs is None:
            return YoloModelResult(
                model_name=model_name,
                model_task=getattr(model, "task", "unknown"),
                predicted_class="unknown",
                confidence=0.0,
                inference_ms=elapsed_ms,
                is_available=True,
                error=f"result.probs is None (model task mismatch: {getattr(model, 'task', '?')})",
            )

        names = result.names
        top1_idx = probs.top1
        top1_conf = float(probs.top1conf)

        all_probs = {}
        for idx, name in names.items():
            all_probs[name] = round(float(probs.data[idx]), 4)

        return YoloModelResult(
            model_name=model_name,
            model_task="classify",
            predicted_class=names[top1_idx],
            confidence=round(top1_conf, 4),
            all_probabilities=all_probs,
            inference_ms=round(elapsed_ms, 2),
            is_available=True,
        )

    except Exception as e:
        logger.error(f"Classification inference error ({model_name}): {e}")
        return YoloModelResult(
            model_name=model_name,
            model_task="classify",
            predicted_class="unknown",
            confidence=0.0,
            inference_ms=0.0,
            is_available=True,
            error=str(e),
        )


def run_detection_jam_inference(model, image_bytes: bytes, model_name: str, jam_roi=None) -> YoloModelResult:
    """Ước lượng dấu hiệu ùn tắc từ mức chiếm chỗ và cụm xe; không đo vận tốc."""
    if model is None:
        return YoloModelResult(
            model_name=model_name,
            model_task="detect",
            predicted_class="unknown",
            confidence=None,
            traffic_jam_status="unverified",
            traffic_jam_evidence="Model chưa được tải hoặc lỗi khởi động.",
            is_available=False,
            error="Jam model not loaded",
        )

    start = time.perf_counter()
    try:
        from PIL import Image, ImageOps

        validate_roi(jam_roi)
        with Image.open(io.BytesIO(image_bytes)) as source:
            img = ImageOps.exif_transpose(source).convert("RGB")
        results = model(img, verbose=False, conf=0.3)
        if not results or results[0].boxes is None:
            raise ValueError("Detection model did not return a boxes result")

        boxes = results[0].boxes
        assessment = assess_geometry(
            boxes.xyxy.cpu().numpy(),
            boxes.cls.cpu().numpy().astype(int),
            img.size,
            roi=jam_roi,
            thresholds=JAM_GEOMETRY_THRESHOLDS,
        )
        metrics = assessment["metrics"]
        return YoloModelResult(
            model_name=f"{model_name} (geometry-heuristic)",
            model_task="detect",
            predicted_class="jam" if assessment["status"] == "suspected" else "normal",
            # Không trình bày điểm heuristic như xác suất nhận diện đã hiệu chuẩn.
            confidence=None,
            traffic_jam_status=assessment["status"],
            traffic_jam_evidence=assessment["evidence"],
            geometry_metrics=JamGeometryMetrics(**metrics),
            all_probabilities={"vehicle_count": float(metrics["vehicle_count"])},
            counts_by_type=assessment["counts_by_type"],
            inference_ms=round((time.perf_counter() - start) * 1000, 2),
            is_available=True,
        )
    except Exception as e:
        logger.error(f"Detection jam inference error ({model_name}): {e}")
        return YoloModelResult(
            model_name=model_name,
            model_task="detect",
            predicted_class="unknown",
            confidence=None,
            traffic_jam_status="unverified",
            traffic_jam_evidence=f"Lỗi suy luận detection: {e}",
            inference_ms=round((time.perf_counter() - start) * 1000, 2),
            is_available=True,
            error=str(e),
        )


def run_accident_fire_inference(image_bytes: bytes) -> YoloModelResult:
    return run_classification_inference(accident_fire_model, image_bytes, "yolo11n-cls-accident-fire")


def run_jam_inference(image_bytes: bytes, jam_roi=None) -> YoloModelResult:
    if jam_model is None:
        return YoloModelResult(
            model_name="yolo11n-jam",
            model_task="unknown",
            predicted_class="unknown",
            confidence=0.0,
            inference_ms=0.0,
            is_available=False,
            error="Jam model not loaded",
        )

    if jam_model_task == "classify":
        result = run_classification_inference(jam_model, image_bytes, "yolo11n-cls-jam")
        if result.error is not None:
            result.traffic_jam_status = "unverified"
        elif result.predicted_class.lower().strip() in ("jam", "traffic_jam", "congestion"):
            result.traffic_jam_status = "suspected"
        elif result.predicted_class.lower().strip() in ("normal", "no_jam", "non_jam"):
            result.traffic_jam_status = "no_evidence"
        else:
            result.traffic_jam_status = "unverified"
        return result
    elif jam_model_task == "detect":
        return run_detection_jam_inference(jam_model, image_bytes, "yolo11n-jam-detect", jam_roi)
    else:
        return YoloModelResult(
            model_name="yolo11n-jam",
            model_task=jam_model_task,
            predicted_class="unknown",
            confidence=0.0,
            inference_ms=0.0,
            is_available=False,
            error=f"Unsupported model task: {jam_model_task}",
        )


# ---------------------------------------------------------------------------
# Merge Results – Phân định rõ ràng 'unverified' vs 'none'
# ---------------------------------------------------------------------------
def merge_results(af_result: YoloModelResult, jam_result: YoloModelResult) -> ClassifyResponse:
    models = [af_result, jam_result]
    total_ms = af_result.inference_ms + jam_result.inference_ms

    af_ok = af_result.is_available and af_result.error is None
    jam_ok = jam_result.is_available and jam_result.error is None

    # ─── Trường hợp 1: CẢ HAI model đều lỗi ───
    if not af_ok and not jam_ok:
        errs = []
        if af_result.error:
            errs.append(f"accident-fire: {af_result.error}")
        if jam_result.error:
            errs.append(f"jam: {jam_result.error}")

        return ClassifyResponse(
            is_error=True,
            error_message=f"Cả hai mô hình YOLO đều không khả dụng ({'; '.join(errs)})",
            is_accident_fire_evaluated=False,
            is_jam_evaluated=False,
            incident_type="unverified",
            incident_confidence=None,
            severity_score=0,
            needs_human_review=True,
            hazard_tags=["yolo_unavailable", "needs_human_review"],
            summary="[YOLO] Lỗi: Cả hai mô hình đều không hoạt động. Chuyển sang Qwen-VL hoặc Điều phối viên.",
            models=models,
            total_inference_ms=round(total_ms, 2),
        )

    # ─── Xử lý kết quả Tai nạn / Hỏa hoạn ───
    incident_type = "unverified"
    incident_confidence: Optional[float] = None
    hazard_tags: list[str] = []
    needs_review = False

    if af_ok:
        pred = af_result.predicted_class.lower().strip()
        incident_confidence = af_result.confidence

        if pred == "fire":
            incident_type = "fire"
            hazard_tags.append("fire")
        elif pred == "accident":
            incident_type = "accident"
            hazard_tags.append("traffic_accident")
        elif pred == "reject":
            # Model classification đã thẩm định và xác nhận là reject (ảnh bình thường / không có sự cố)
            incident_type = "none"
        else:
            logger.warning(f"Lớp dự đoán không xác định từ accident-fire: '{pred}'")
            incident_type = "unverified"
            needs_review = True
            hazard_tags.append("unverified_class")
    else:
        # Quan trọng: Model tai nạn/cháy hỏng KHÔNG ĐƯỢC kết luận là "none"!
        incident_type = "unverified"
        incident_confidence = None
        needs_review = True
        hazard_tags.extend(["accident_fire_unavailable", "needs_human_review"])
        logger.warning("Accident-fire model không khả dụng. Đánh dấu incident_type = unverified.")

    # ─── Xử lý kết quả Ùn tắc giao thông (Trạng thái nhiều mức) ───
    traffic_jam_status = "unverified"
    traffic_jam_evidence = None
    is_jam_heuristic = (jam_result.model_task == "detect")
    jam_vehicle_count: Optional[int] = None
    jam_counts_by_type: dict[str, int] = {}
    jam_conf: Optional[float] = None
    jam_geometry = None

    if jam_ok:
        jam_conf = None if is_jam_heuristic else jam_result.confidence
        traffic_jam_status = jam_result.traffic_jam_status or ("suspected" if jam_result.predicted_class in ("jam", "traffic_jam", "congestion") else "no_evidence")
        traffic_jam_evidence = jam_result.traffic_jam_evidence

        if is_jam_heuristic:
            if "vehicle_count" in jam_result.all_probabilities:
                jam_vehicle_count = int(jam_result.all_probabilities["vehicle_count"])
            jam_counts_by_type = jam_result.counts_by_type
            jam_geometry = jam_result.geometry_metrics

        if traffic_jam_status == "suspected":
            hazard_tags.append("suspected_traffic_jam")
            needs_review = True
        elif traffic_jam_status == "unverified":
            needs_review = True
    else:
        traffic_jam_status = "unverified"
        traffic_jam_evidence = "Model ùn tắc không khả dụng hoặc lỗi suy luận."
        hazard_tags.append("jam_model_unavailable")
        needs_review = True

    # ─── Severity ───
    # YOLO KHÔNG tự định nghĩa severity thực tế (mức 1-3).
    # Luôn trả severity_score = 0 (Unclassified). Việc xác định mức độ 1-3 là của Qwen-VL hoặc Điều phối viên.
    severity_score = 0
    if incident_type in ("fire", "accident") or needs_review:
        needs_review = True
        if "needs_human_review" not in hazard_tags:
            hazard_tags.append("needs_human_review")

    # ─── Xây dựng tóm tắt (Summary) minh bạch ───
    summary_parts = []
    if af_ok:
        if incident_type == "fire":
            summary_parts.append(f"[YOLO] Phát hiện hỏa hoạn (độ tin cậy: {incident_confidence:.1%})")
        elif incident_type == "accident":
            summary_parts.append(f"[YOLO] Phát hiện tai nạn giao thông (độ tin cậy: {incident_confidence:.1%})")
        elif incident_type == "none":
            summary_parts.append("[YOLO] Đã kiểm tra: Không phát hiện tai nạn hoặc hỏa hoạn (reject)")
        else:
            summary_parts.append(f"[YOLO] CẢNH BÁO: Không xác định được lớp '{af_result.predicted_class}', cần thẩm định")
    else:
        summary_parts.append("[YOLO] CẢNH BÁO: Model tai nạn/hỏa hoạn gặp sự cố, chưa được đánh giá")

    if jam_ok:
        if traffic_jam_status == "suspected":
            detail_str = ""
            if jam_counts_by_type:
                active_counts = [f"{k}: {v}" for k, v in jam_counts_by_type.items() if v > 0]
                if active_counts:
                    detail_str = f" [{', '.join(active_counts)}]"
            note = f" (heuristic {jam_vehicle_count} xe{detail_str})" if is_jam_heuristic else ""
            summary_parts.append(traffic_jam_evidence or f"Nghi ngờ ùn tắc giao thông{note}, cần xác minh")
        elif traffic_jam_status == "no_evidence":
            summary_parts.append("Chưa phát hiện dấu hiệu ùn tắc trên ảnh")
        else:
            summary_parts.append("Chưa thể đánh giá ùn tắc giao thông")
    else:
        summary_parts.append("Model ùn tắc không khả dụng")

    summary_parts.append("Mức độ nghiêm trọng (Severity) chưa thẩm định (Severity=0).")
    summary = ". ".join(summary_parts)

    return ClassifyResponse(
        is_error=False,
        error_message=None,
        is_accident_fire_evaluated=af_ok,
        is_jam_evaluated=jam_ok,
        incident_type=incident_type,
        incident_confidence=incident_confidence,
        traffic_jam_status=traffic_jam_status,
        traffic_jam_evidence=traffic_jam_evidence,
        is_traffic_jam=False,  # Luôn là False ở tầng AI, chỉ True khi con người Confirmed
        jam_confidence=jam_conf,
        is_jam_heuristic=is_jam_heuristic,
        jam_vehicle_count=jam_vehicle_count,
        jam_counts_by_type=jam_counts_by_type,
        jam_geometry=jam_geometry,
        severity_score=severity_score,
        needs_human_review=needs_review,
        hazard_tags=hazard_tags,
        summary=summary,
        models=models,
        total_inference_ms=round(total_ms, 2),
    )


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------

@app.post("/api/yolo/classify", response_model=ClassifyResponse)
async def classify_image(request: ClassifyRequest):
    if not request.image_url and not request.image_base64:
        raise HTTPException(status_code=400, detail="Either image_url or image_base64 is required")
    if request.jam_roi is not None and jam_model_task == "classify":
        raise HTTPException(status_code=400, detail="jam_roi requires a detection model")

    try:
        if request.image_url:
            image_bytes = await download_image(request.image_url)
        else:
            image_bytes = decode_base64_image(request.image_base64)  # type: ignore
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Failed to load image: {e}")

    loop = asyncio.get_event_loop()
    af_task = loop.run_in_executor(None, run_accident_fire_inference, image_bytes)
    jam_task = loop.run_in_executor(None, run_jam_inference, image_bytes, request.jam_roi)

    af_result, jam_result = await asyncio.gather(af_task, jam_task)
    return merge_results(af_result, jam_result)


@app.get("/api/yolo/health", response_model=HealthResponse)
async def health_check():
    """
    Health check logic:
    - "healthy": CẢ HAI mô hình đều tải thành công
    - "degraded": Chỉ 1 mô hình tải thành công (thiếu 1 chức năng)
    - "unhealthy": Cả hai mô hình đều không hoạt động
    """
    af_ready = accident_fire_model is not None
    jam_ready = jam_model is not None

    if af_ready and jam_ready:
        status = "healthy"
    elif af_ready or jam_ready:
        status = "degraded"
    else:
        status = "unhealthy"

    af_info = f"task={af_model_task}, classes={list(af_model_names.values())}" if af_ready else "not loaded"
    jam_info = f"task={jam_model_task}, classes={list(jam_model_names.values())}" if jam_ready else "not loaded"

    return HealthResponse(
        status=status,
        is_accident_fire_ready=af_ready,
        is_jam_ready=jam_ready,
        models_loaded={
            "accident_fire": af_ready,
            "traffic_jam": jam_ready,
        },
        models_info={
            "accident_fire": af_info,
            "traffic_jam": jam_info,
        },
        gpu_available=device_info == "cuda",
    )


if __name__ == "__main__":
    port = int(os.getenv("YOLO_SERVICE_PORT", "8001"))
    uvicorn.run("main:app", host="0.0.0.0", port=port, reload=False, workers=1)

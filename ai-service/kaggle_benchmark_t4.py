# =============================================================================
#  SEP490 — Đo độ trễ TriageVL-3B trên Kaggle T4 (TC_F5_PERF_03, Table 50a R9) và lưu log làm minh chứng.
#  Chạy ở CELL THỨ HAI, sau khi cell kaggle_serve_ngrok.py đã in "health: healthy" (dùng lại PORT, API_KEY,
#  public_url của cell đó).
#
#  Ảnh test: Add Input → dataset ảnh test của nhóm (hoặc bất kỳ dataset ảnh cháy / tai nạn nào).
#  Đổi IMAGE_GLOB nếu ảnh nằm chỗ khác. Kết quả: /kaggle/working/benchmark_t4/
#    latency.csv   — từng request: tên ảnh, thời gian đo phía client, inference_time_ms phía service, kết quả
#    vram.csv      — mẫu nvidia-smi mỗi 0,5 giây
#    summary.json  — mean / median / p95 / max, VRAM đỉnh, GPU, phiên bản gói, cấu hình
#  Sau khi chạy: bấm "Save Version" (Quick Save) để Kaggle lưu output, rồi tải thư mục benchmark_t4 về
#  emergency-dispatch-backend/loadtest/results/ai_t4_<ngày>/.
# =============================================================================
import csv
import glob
import json
import os
import random
import statistics
import subprocess
import threading
import time
import urllib.request
import uuid

N_REQUESTS = int(os.environ.get("BENCH_N", "120"))
N_WARMUP = 3                                  # không tính vào kết quả
N_NGROK = int(os.environ.get("BENCH_NGROK_N", "10"))   # đo thêm qua ngrok (end-to-end), 0 = bỏ qua
IMAGE_GLOB = os.environ.get("BENCH_IMAGES", "/kaggle/input/**/*.jpg")
OUT = "/kaggle/working/benchmark_t4"
os.makedirs(OUT, exist_ok=True)

images = sorted(glob.glob(IMAGE_GLOB, recursive=True)) + sorted(glob.glob(IMAGE_GLOB.replace(".jpg", ".png"), recursive=True))
if len(images) < N_REQUESTS + N_WARMUP:
    raise SystemExit(f"Cần ít nhất {N_REQUESTS + N_WARMUP} ảnh, tìm thấy {len(images)} theo {IMAGE_GLOB}")
random.seed(490)
sample = random.sample(images, N_REQUESTS + N_WARMUP + max(N_NGROK, 0))


def post_image(base_url, path):
    """Gửi multipart giống backend; trả (giây đo phía client, JSON)."""
    boundary = uuid.uuid4().hex
    with open(path, "rb") as f:
        data = f.read()
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{os.path.basename(path)}\"\r\n"
            f"Content-Type: image/jpeg\r\n\r\n").encode() + data + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(f"{base_url}/api/analyze-incident", data=body, method="POST", headers={
        "Content-Type": f"multipart/form-data; boundary={boundary}", "X-AI-Service-Key": API_KEY,  # noqa: F821
        "Accept-Language": "vi", "ngrok-skip-browser-warning": "1"})
    t0 = time.perf_counter()
    with urllib.request.urlopen(req, timeout=300) as r:
        payload = json.loads(r.read())
    return time.perf_counter() - t0, payload


# Lấy mẫu VRAM bằng nvidia-smi trong lúc đo
vram_rows, stop = [], threading.Event()


def sample_vram():
    while not stop.is_set():
        out = subprocess.run(["nvidia-smi", "--query-gpu=index,memory.used,memory.total,utilization.gpu",
                              "--format=csv,noheader,nounits"], capture_output=True, text=True).stdout
        for line in out.strip().splitlines():
            vram_rows.append([time.strftime("%H:%M:%S")] + [x.strip() for x in line.split(",")])
        stop.wait(0.5)


local = f"http://127.0.0.1:{PORT}"  # noqa: F821
for p in sample[:N_WARMUP]:
    post_image(local, p)
print(f"warm-up xong ({N_WARMUP} request). Đo {N_REQUESTS} request tuần tự …")

threading.Thread(target=sample_vram, daemon=True).start()
rows = []
for i, p in enumerate(sample[N_WARMUP:N_WARMUP + N_REQUESTS], 1):
    try:
        sec, res = post_image(local, p)
        rows.append([i, os.path.basename(p), "local", round(sec * 1000), res.get("inference_time_ms"),
                     res.get("incident_type"), res.get("severity_level"), res.get("confidence"), "ok"])
    except Exception as e:  # ghi lại cả lỗi, không bỏ qua
        rows.append([i, os.path.basename(p), "local", None, None, None, None, None, f"error: {e}"])
    if i % 20 == 0:
        print(f"  {i}/{N_REQUESTS}")
for j, p in enumerate(sample[N_WARMUP + N_REQUESTS:N_WARMUP + N_REQUESTS + max(N_NGROK, 0)], 1):
    try:
        sec, res = post_image(public_url, p)  # noqa: F821
        rows.append([j, os.path.basename(p), "ngrok", round(sec * 1000), res.get("inference_time_ms"),
                     res.get("incident_type"), res.get("severity_level"), res.get("confidence"), "ok"])
    except Exception as e:
        rows.append([j, os.path.basename(p), "ngrok", None, None, None, None, None, f"error: {e}"])
stop.set()
time.sleep(1)

with open(f"{OUT}/latency.csv", "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["i", "image", "path", "client_ms", "service_inference_ms", "incident_type", "severity_level", "confidence", "status"])
    w.writerows(rows)
with open(f"{OUT}/vram.csv", "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["time", "gpu", "memory_used_mib", "memory_total_mib", "util_pct"])
    w.writerows(vram_rows)


def stats(vals):
    vals = sorted(v for v in vals if v is not None)
    if not vals:
        return None
    return {"n": len(vals), "mean_s": round(statistics.mean(vals) / 1000, 2), "median_s": round(statistics.median(vals) / 1000, 2),
            "p95_s": round(vals[min(len(vals) - 1, int(0.95 * len(vals) + 0.999) - 1)] / 1000, 2), "max_s": round(vals[-1] / 1000, 2)}


def pkg(name):
    try:
        from importlib.metadata import version
        return version(name)
    except Exception:
        return None


health = json.loads(urllib.request.urlopen(f"{local}/health", timeout=10).read())
summary = {
    "measured_at_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    "gpu": health.get("gpu_name"), "service_version": health.get("service_version"),
    "max_image_side": globals().get("MAX_IMAGE_SIDE"), "warmup_excluded": N_WARMUP,
    "local_client": stats([r[3] for r in rows if r[2] == "local"]),
    "local_service_inference": stats([r[4] for r in rows if r[2] == "local"]),
    "ngrok_end_to_end": stats([r[3] for r in rows if r[2] == "ngrok"]),
    "errors": sum(1 for r in rows if r[8] != "ok"),
    "peak_vram_mib_nvidia_smi": max((int(r[2]) for r in vram_rows if r[2].isdigit()), default=None),
    "packages": {k: pkg(k) for k in ["torch", "transformers", "peft", "bitsandbytes", "accelerate", "fastapi"]},
}
with open(f"{OUT}/summary.json", "w") as f:
    json.dump(summary, f, indent=2, ensure_ascii=False)
print(json.dumps(summary, indent=2, ensure_ascii=False))
print(f"\n→ Bấm Save Version để lưu, rồi tải {OUT}/ về loadtest/results/ai_t4_<ngày>/")

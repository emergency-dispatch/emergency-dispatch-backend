# =============================================================================
#  SEP490 — Chạy AI service TriageVL-3B (Qwen2.5-VL-3B + LoRA) trên Kaggle và
#  mở ra Internet qua ngrok để backend ASP.NET Core gọi tới.
#  Dán TOÀN BỘ file này vào 1 cell Kaggle rồi Run.
# -----------------------------------------------------------------------------
#  CHUẨN BỊ (làm 1 lần):
#   1. Tạo Kaggle Dataset (VD: "triagevl-service") gồm:
#        - ai_service.py              (emergency-dispatch-backend/ai-service/ai_service.py)
#        - lora_adapter/              (thư mục adapter của nhóm, ~127 MB — không có trong git)
#   2. Notebook: Add Input → dataset trên. Settings: Accelerator = GPU T4 x2, Internet = On.
#   3. Add-ons → Secrets:
#        - NGROK_AUTHTOKEN     (bắt buộc, lấy ở dashboard.ngrok.com)
#        - AI_SERVICE_API_KEY  (nên có; thiếu thì cell tự sinh và in ra)
#        - NGROK_DOMAIN        (tùy chọn: domain tĩnh miễn phí của ngrok để URL không đổi)
#  SAU KHI CHẠY: chép URL + khóa in ở cuối vào appsettings.json của backend.
#  Lưu ý: phiên Kaggle tự tắt sau tối đa ~12 giờ; tắt notebook là API ngừng.
#  Backend tự chuyển sang OpenRouter → Unclassified khi không gọi được service.
# =============================================================================
import glob
import json
import os
import secrets
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request

PORT = 8000
WORK_DIR = "/kaggle/working/triagevl"
LOG_PATH = os.path.join(WORK_DIR, "ai_service.log")
MAX_IMAGE_SIDE = "512"          # GPU Kaggle đủ lớn → dùng 512 px giống lúc đánh giá
MODEL_LOAD_TIMEOUT_S = 20 * 60  # lần đầu phải tải base model từ Hugging Face

# Phiên bản đã chạy được với ai_service.py ở máy local (29/10/2026)
PACKAGES = [
    "transformers==5.19.0", "peft==0.21.2", "bitsandbytes==0.50.2", "qwen-vl-utils==0.0.14",
    "accelerate==1.15.0", "fastapi==0.143.1", "uvicorn==0.54.0", "python-multipart==0.0.32",
    "pyngrok",
]

print("📦 [1/6] Cài thư viện...")
subprocess.run([sys.executable, "-m", "pip", "install", "-q", *PACKAGES], check=True)

print("🔑 [2/6] Đọc Kaggle Secrets...")
from kaggle_secrets import UserSecretsClient  # noqa: E402

_secrets = UserSecretsClient()


def get_secret(name):
    try:
        return (_secrets.get_secret(name) or "").strip()
    except Exception:
        return ""


NGROK_AUTHTOKEN = get_secret("NGROK_AUTHTOKEN")
NGROK_DOMAIN = get_secret("NGROK_DOMAIN")
API_KEY = get_secret("AI_SERVICE_API_KEY")
if not NGROK_AUTHTOKEN:
    raise SystemExit("❌ Thiếu secret NGROK_AUTHTOKEN (Add-ons → Secrets).")
if not API_KEY:
    API_KEY = secrets.token_urlsafe(32)
    print("   ⚠️ Chưa có secret AI_SERVICE_API_KEY → đã sinh khóa tạm (in ở cuối). Phiên sau sẽ đổi khóa.")

print("📂 [3/6] Tìm ai_service.py và lora_adapter trong /kaggle/input...")
service_files = glob.glob("/kaggle/input/**/ai_service.py", recursive=True)
adapter_cfgs = glob.glob("/kaggle/input/**/lora_adapter/adapter_config.json", recursive=True)
if not service_files or not adapter_cfgs:
    raise SystemExit("❌ Không thấy ai_service.py hoặc lora_adapter/adapter_config.json. Kiểm tra dataset đã Add Input chưa.")
adapter_dir = os.path.dirname(adapter_cfgs[0])
os.makedirs(WORK_DIR, exist_ok=True)
shutil.copy(service_files[0], os.path.join(WORK_DIR, "ai_service.py"))
print(f"   service : {service_files[0]}\n   adapter : {adapter_dir}")

print("🚀 [4/6] Khởi động AI service (nạp model, lần đầu mất vài phút)...")
env = dict(os.environ,
           AI_LORA_ADAPTER_DIR=adapter_dir,
           AI_SERVICE_API_KEY=API_KEY,
           AI_MAX_IMAGE_SIDE=MAX_IMAGE_SIDE,
           AI_ALLOW_LOCAL_PATHS="0",       # public: không cho đọc file trên máy Kaggle
           PYTHONUNBUFFERED="1")
log_file = open(LOG_PATH, "w")
server = subprocess.Popen(
    [sys.executable, "-m", "uvicorn", "ai_service:app", "--host", "0.0.0.0", "--port", str(PORT)],
    cwd=WORK_DIR, env=env, stdout=log_file, stderr=subprocess.STDOUT)


def health():
    with urllib.request.urlopen(f"http://127.0.0.1:{PORT}/health", timeout=5) as r:
        return json.loads(r.read())


deadline = time.time() + MODEL_LOAD_TIMEOUT_S
while True:
    if server.poll() is not None:
        log_file.flush()
        print(open(LOG_PATH).read()[-4000:])
        raise SystemExit("❌ AI service đã dừng khi khởi động (log ở trên).")
    try:
        h = health()
        if h.get("model_ready"):
            print(f"   ✅ Model sẵn sàng trên {h.get('gpu_name')}")
            break
    except (urllib.error.URLError, ConnectionError, OSError):
        pass
    if time.time() > deadline:
        server.terminate()
        raise SystemExit(f"❌ Quá {MODEL_LOAD_TIMEOUT_S // 60} phút chưa nạp xong model. Xem {LOG_PATH}.")
    time.sleep(10)

print("🌐 [5/6] Mở tunnel ngrok...")
from pyngrok import conf, ngrok  # noqa: E402

conf.get_default().auth_token = NGROK_AUTHTOKEN
tunnel = ngrok.connect(PORT, "http", **({"domain": NGROK_DOMAIN} if NGROK_DOMAIN else {}))
public_url = tunnel.public_url.replace("http://", "https://")

print("🧪 [6/6] Kiểm tra: gọi không có khóa phải bị từ chối (401)...")
try:
    urllib.request.urlopen(urllib.request.Request(
        f"{public_url}/api/analyze-incident", data=b"", method="POST",
        headers={"ngrok-skip-browser-warning": "true"}), timeout=30)
    print("   ⚠️ Gọi không khóa vẫn thành công — kiểm tra lại AI_SERVICE_API_KEY!")
except urllib.error.HTTPError as e:
    print(f"   {'✅' if e.code == 401 else '⚠️'} HTTP {e.code}")

print("\n" + "=" * 78)
print(" DÁN VÀO emergency-dispatch-backend/EmergencyDispatch.API/appsettings.json:")
print("=" * 78)
print(json.dumps({"AiService": {
    "UseLocalModel": True,
    "LocalServiceUrl": f"{public_url}/api/analyze-incident",
    "LocalTimeoutSeconds": 120,
    "ApiKey": API_KEY,
}}, indent=2))
print("=" * 78)
print(" Giữ notebook chạy. Dừng cell (■) để tắt service và tunnel.\n")

try:
    while server.poll() is None:
        time.sleep(300)
        try:
            h = health()
            print(time.strftime("%H:%M:%S"), "health:", h.get("status"))
        except Exception as e:
            print(time.strftime("%H:%M:%S"), "health lỗi:", e)
    print("❌ AI service đã dừng. Log cuối:\n", open(LOG_PATH).read()[-3000:])
finally:
    ngrok.kill()
    if server.poll() is None:
        server.terminate()

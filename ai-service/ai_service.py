import os
import re
import sys
import time
import json
import io
import torch
import torch.nn as nn
from typing import Optional, List
from contextlib import asynccontextmanager
import hmac
from fastapi import FastAPI, UploadFile, File, Form, HTTPException, Header, Depends
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from PIL import Image
from transformers import Qwen2_5_VLForConditionalGeneration, AutoProcessor
from bitsandbytes.nn.modules import Linear4bit
from peft import PeftModel
import peft.tuners.lora.bnb as peft_bnb
from qwen_vl_utils import process_vision_info

@asynccontextmanager
async def lifespan(_app: FastAPI):
    # Khởi động và nạp mô hình vào GPU khi server bật
    load_ai_model()
    yield

# Khởi tạo FastAPI App
app = FastAPI(
    title="SEP490 Emergency Dispatch AI Service",
    description="Microservice phân tích sự cố khẩn cấp sử dụng Qwen2.5-VL-3B LoRA Adapter (Local GPU)",
    version="1.0.0",
    lifespan=lifespan
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Cấu hình đường dẫn model
BASE_MODEL_ID = "unsloth/Qwen2.5-VL-3B-Instruct-unsloth-bnb-4bit"
LORA_ADAPTER_DIR = os.path.abspath(os.environ.get(
    "AI_LORA_ADAPTER_DIR", os.path.join(os.path.dirname(__file__), "lora_adapter")))
DEVICE = "cuda:0" if torch.cuda.is_available() else "cpu"

# Cấu hình khi đưa service ra Internet (Kaggle + ngrok). Mặc định giữ nguyên hành vi chạy local.
#   AI_SERVICE_API_KEY   : bắt buộc header X-AI-Service-Key đúng khóa này (trống = không kiểm tra)
#   AI_MAX_IMAGE_SIDE    : cạnh ảnh tối đa (384 cho GPU 4GB; 512 giống lúc đánh giá nếu GPU đủ lớn)
#   AI_ALLOW_LOCAL_PATHS : "1" cho phép image_url là đường dẫn file trên máy; tắt khi public
API_KEY = os.environ.get("AI_SERVICE_API_KEY", "").strip()
MAX_IMAGE_SIDE = int(os.environ.get("AI_MAX_IMAGE_SIDE", "384"))
ALLOW_LOCAL_PATHS = os.environ.get("AI_ALLOW_LOCAL_PATHS", "1") == "1"


def require_api_key(x_ai_service_key: Optional[str] = Header(None)):
    if API_KEY and not hmac.compare_digest(x_ai_service_key or "", API_KEY):
        raise HTTPException(status_code=401, detail="Sai hoặc thiếu X-AI-Service-Key.")

TRAIN_PROMPT = (
    "Phân tích ảnh hiện trường cho hệ thống điều phối khẩn cấp SEP490. "
    "Trả về DUY NHẤT JSON: {\"incident_type\": \"fire|accident|none\", \"severity_level\": 0-3, "
    "\"traffic_jam\": true|false, \"tags\": [\"...\"], \"description\": \"1-2 câu tiếng Việt\"}"
)

# Biến toàn cục lưu model và processor
model = None
processor = None
is_model_ready = False

def replace_unquantized_linear4bit(target_model):
    """
    Chuyển đổi các layer Linear4bit chưa nén 4-bit (giữ ma trận 2D FP16)
    sang nn.Linear chuẩn để tránh lỗi assert bitsandbytes trên Windows.
    """
    converted = 0
    for name, mod in list(target_model.named_modules()):
        if isinstance(mod, Linear4bit) and hasattr(mod.weight, "shape"):
            if len(mod.weight.shape) == 2 and mod.weight.shape[1] != 1:
                parts = name.split(".")
                parent = target_model
                for p in parts[:-1]:
                    parent = getattr(parent, p)
                attr_name = parts[-1]
                
                has_bias = mod.bias is not None
                linear = nn.Linear(
                    mod.in_features,
                    mod.out_features,
                    bias=has_bias,
                    device=mod.weight.device,
                    dtype=mod.weight.dtype
                )
                linear.weight.data.copy_(mod.weight.data)
                if has_bias:
                    linear.bias.data.copy_(mod.bias.data)
                setattr(parent, attr_name, linear)
                converted += 1
    return converted

def safe_patch_peft():
    """Vá tương thích giữa bitsandbytes 4-bit của Unsloth và thư viện PEFT"""
    orig_dispatch = peft_bnb.dispatch_bnb_4bit
    def safe_dispatch_bnb_4bit(target, adapter_name, config, **kwargs):
        if hasattr(target, "weight"):
            if not hasattr(target.weight, "compress_statistics"):
                target.weight.compress_statistics = False
            if not hasattr(target.weight, "quant_type"):
                target.weight.quant_type = "nf4"
        return orig_dispatch(target, adapter_name, config, **kwargs)
    peft_bnb.dispatch_bnb_4bit = safe_dispatch_bnb_4bit

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

def load_ai_model():
    global model, processor, is_model_ready
    print(f"[AI Service] Dang nap Processor tu: {LORA_ADAPTER_DIR}")
    processor = AutoProcessor.from_pretrained(LORA_ADAPTER_DIR)

    print(f"[AI Service] Dang nap Base Model: {BASE_MODEL_ID} (4-bit, Device: {DEVICE})...")
    base_model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
        BASE_MODEL_ID,
        device_map="auto" if DEVICE.startswith("cuda") else None,
        torch_dtype=torch.float16 if DEVICE.startswith("cuda") else torch.float32
    )

    conv_count = replace_unquantized_linear4bit(base_model)
    print(f"[AI Service] Da chuyen doi {conv_count} layer sang nn.Linear chuan.")

    safe_patch_peft()
    print(f"[AI Service] Dang ghep noi LoRA Adapter tu: {LORA_ADAPTER_DIR}...")
    model = PeftModel.from_pretrained(base_model, LORA_ADAPTER_DIR)
    model.eval()
    is_model_ready = True
    print("[AI Service] Model Qwen2.5-VL-3B LoRA da san sang phuc vu suy luan!")

# Pydantic schemas
class AnalyzeResponse(BaseModel):
    is_emergency: bool
    incident_type: str
    severity_level: int
    traffic_jam: bool
    tags: List[str]
    description: str
    confidence: float
    raw_response: str
    inference_time_ms: int
    model_name: str = "Qwen2.5-VL-3B-LoRA"

@app.get("/health")
def health_check():
    return {
        "status": "healthy" if is_model_ready else "loading",
        "device": DEVICE,
        "cuda_available": torch.cuda.is_available(),
        "gpu_name": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
        "lora_adapter": LORA_ADAPTER_DIR,
        "model_ready": is_model_ready
    }

def extract_json(text: str) -> dict:
    """Trích xuất và parse JSON an toàn từ kết quả sinh của mô hình"""
    clean_text = text.strip()
    if "```json" in clean_text:
        match = re.search(r"```json\s*(\{.*?\})\s*```", clean_text, re.DOTALL)
        if match:
            clean_text = match.group(1)
    elif "```" in clean_text:
        match = re.search(r"```\s*(\{.*?\})\s*```", clean_text, re.DOTALL)
        if match:
            clean_text = match.group(1)

    try:
        return json.loads(clean_text)
    except Exception:
        start_idx = clean_text.find("{")
        end_idx = clean_text.rfind("}")
        if start_idx != -1 and end_idx != -1 and end_idx > start_idx:
            try:
                return json.loads(clean_text[start_idx:end_idx+1])
            except Exception:
                pass
    
    return {
        "incident_type": "none",
        "severity_level": 0,
        "traffic_jam": False,
        "tags": ["unparseable"],
        "description": clean_text[:200]
    }

@app.post("/api/analyze-incident", response_model=AnalyzeResponse, dependencies=[Depends(require_api_key)])
async def analyze_incident(
    image: Optional[UploadFile] = File(None),
    image_url: Optional[str] = Form(None),
    citizen_note: Optional[str] = Form(None)
):
    """
    Phân tích ảnh hiện trường và bối cảnh sự cố bằng mô hình Qwen2.5-VL LoRA
    """
    if not is_model_ready:
        raise HTTPException(status_code=503, detail="Mô hình AI đang khởi động, vui lòng thử lại sau giây lát.")

    start_time = time.time()
    pil_image = None

    try:
        if image is not None:
            contents = await image.read()
            pil_image = Image.open(io.BytesIO(contents)).convert("RGB")
        elif image_url:
            clean_url = image_url.strip().strip('"').strip("'")
            if ALLOW_LOCAL_PATHS and os.path.exists(clean_url):
                pil_image = Image.open(clean_url).convert("RGB")
            elif not clean_url.lower().startswith(("http://", "https://")):
                raise HTTPException(status_code=400, detail="image_url phải là URL http(s).")
            else:
                import urllib.request
                req = urllib.request.Request(clean_url, headers={"User-Agent": "Mozilla/5.0"})
                with urllib.request.urlopen(req) as resp:
                    data = resp.read()
                    pil_image = Image.open(io.BytesIO(data)).convert("RGB")
        else:
            raise HTTPException(status_code=400, detail="Cần cung cấp file ảnh (image) hoặc đường link ảnh (image_url).")
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Không đọc được hình ảnh: {str(e)}")

    max_side = MAX_IMAGE_SIDE
    if max(pil_image.size) > max_side:
        pil_image.thumbnail((max_side, max_side), Image.Resampling.LANCZOS)

    prompt = TRAIN_PROMPT
    if citizen_note:
        prompt += f" Bối cảnh người dân báo: {citizen_note}"

    messages = [
        {
            "role": "user",
            "content": [
                {"type": "image", "image": pil_image},
                {"type": "text", "text": prompt}
            ]
        }
    ]

    try:
        text_input = processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        # Tùy phiên bản qwen_vl_utils trả 2 hoặc 3 giá trị (thêm video_kwargs) → chỉ lấy 2 phần đầu
        vision_info = process_vision_info(messages)
        image_inputs, video_inputs = vision_info[0], vision_info[1]
        inputs = processor(
            text=[text_input],
            images=image_inputs,
            videos=video_inputs,
            padding=True,
            return_tensors="pt"
        )
        inputs = inputs.to(DEVICE)

        with torch.no_grad():
            generated_ids = model.generate(
                **inputs,
                max_new_tokens=150,
                do_sample=False
            )

        generated_ids_trimmed = [
            out_ids[len(in_ids):] for in_ids, out_ids in zip(inputs.input_ids, generated_ids)
        ]
        output_text = processor.batch_decode(
            generated_ids_trimmed,
            skip_special_tokens=True,
            clean_up_tokenization_spaces=False
        )[0]

        inference_time_ms = int((time.time() - start_time) * 1000)
        parsed = extract_json(output_text)

        incident_type = str(parsed.get("incident_type", "none")).lower()
        severity_level = int(parsed.get("severity_level", 0))
        traffic_jam = bool(parsed.get("traffic_jam", False))
        tags = parsed.get("tags", [])
        if not isinstance(tags, list):
            tags = [str(tags)]
        description = str(parsed.get("description", ""))

        is_emergency = incident_type in ["fire", "accident"] and severity_level > 0
        confidence = 0.95 if is_emergency else 0.85

        return AnalyzeResponse(
            is_emergency=is_emergency,
            incident_type=incident_type,
            severity_level=severity_level,
            traffic_jam=traffic_jam,
            tags=tags,
            description=description,
            confidence=confidence,
            raw_response=output_text,
            inference_time_ms=inference_time_ms,
            model_name="Qwen2.5-VL-3B-LoRA"
        )

    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Lỗi suy luận AI: {str(e)}")
    finally:
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

if __name__ == "__main__":
    import uvicorn
    # Chạy uvicorn trực tiếp với đối tượng app
    uvicorn.run(app, host="0.0.0.0", port=8000)

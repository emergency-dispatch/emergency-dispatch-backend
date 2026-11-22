"""Hai model CNN phân 7 mức (cháy / tai nạn L1–L3 + none) dùng cho chế độ "3 AI bỏ phiếu" của AI tự điều phối.

- ResNet-50 (TrainAI/kaggle/train_resNET50_single_cell.py, train 01/11/2026): backbone ResNet-50 + head_sev (7 lớp)
  + head_jam (kẹt xe); ảnh resize 512x512, chuẩn hoá ImageNet.
- YOLO11n-cls 7 lớp (train 31/10/2026, imgsz 224): ultralytics tự tiền xử lý.

Độ tin cậy là xác suất softmax của lớp dự đoán (đo từ model, không phải model tự khai).
Hai model này KHÔNG phải bản trong paper (paper dùng lần train 08/11 trên tập chia theo nhóm).
"""
import io
import logging
import os
import time

logger = logging.getLogger("yolo-service")

# Thứ tự lớp đúng như lúc train
RESNET_LABELS = ["fire_L1", "fire_L2", "fire_L3", "accident_L1", "accident_L2", "accident_L3", "none"]
VALID_LABELS = set(RESNET_LABELS)
RESNET_IMAGE_SIZE = 512
IMAGENET_MEAN, IMAGENET_STD = (0.485, 0.456, 0.406), (0.229, 0.224, 0.225)

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
MODELS_DIR = os.path.join(BASE_DIR, "models")
RESNET_PATH = os.getenv("TRIAGE_RESNET_MODEL", os.path.join(MODELS_DIR, "triage_resnet50.pt"))
YOLO7_PATH = os.getenv("TRIAGE_YOLO_MODEL", os.path.join(MODELS_DIR, "triage_yolo11n_cls7.pt"))

RESNET_NAME = "ResNet-50 (7 levels)"
YOLO7_NAME = "YOLO11n-cls (7 levels)"

_resnet = None
_resnet_tf = None
_yolo7 = None
_device = "cpu"


def load_triage_models(device: str) -> None:
    """Nạp 2 model; model nào lỗi / thiếu file thì để None (endpoint trả is_available=false cho model đó)."""
    global _resnet, _resnet_tf, _yolo7, _device
    _device = device
    try:
        import torch
        from torch import nn
        from torchvision import models, transforms

        class ResNet50Triage(nn.Module):
            def __init__(self):
                super().__init__()
                base = models.resnet50(weights=None)
                in_f = base.fc.in_features
                base.fc = nn.Identity()
                self.backbone = base
                self.dropout = nn.Dropout(0.3)
                self.head_sev = nn.Linear(in_f, len(RESNET_LABELS))
                self.head_jam = nn.Linear(in_f, 1)

            def forward(self, x):
                feat = self.backbone(x)
                return self.head_sev(feat), self.head_jam(feat).squeeze(1)

        if os.path.exists(RESNET_PATH):
            model = ResNet50Triage()
            state = torch.load(RESNET_PATH, map_location="cpu", weights_only=True)
            model.load_state_dict(state)
            _resnet = model.to(device).eval()
            _resnet_tf = transforms.Compose([
                transforms.Resize((RESNET_IMAGE_SIZE, RESNET_IMAGE_SIZE)),
                transforms.ToTensor(),
                transforms.Normalize(IMAGENET_MEAN, IMAGENET_STD),
            ])
            logger.info(f"✅ Triage ResNet-50 (7 mức) loaded từ: {RESNET_PATH}")
        else:
            logger.warning(f"⚠️ Triage ResNet-50 NOT found at: {RESNET_PATH}")
    except Exception as e:
        _resnet = None
        logger.error(f"❌ Không nạp được Triage ResNet-50: {e}")

    try:
        from ultralytics import YOLO

        if os.path.exists(YOLO7_PATH):
            candidate = YOLO(YOLO7_PATH)
            names = {str(v) for v in getattr(candidate, "names", {}).values()}
            if getattr(candidate, "task", None) == "classify" and names == VALID_LABELS:
                _yolo7 = candidate
                logger.info(f"✅ Triage YOLO11n-cls (7 mức) loaded từ: {YOLO7_PATH}")
            else:
                logger.error(f"❌ {YOLO7_PATH}: task={getattr(candidate, 'task', None)}, nhãn {names} không khớp 7 lớp.")
        else:
            logger.warning(f"⚠️ Triage YOLO11n-cls NOT found at: {YOLO7_PATH}")
    except Exception as e:
        _yolo7 = None
        logger.error(f"❌ Không nạp được Triage YOLO11n-cls: {e}")


def models_status() -> dict:
    return {"triage_resnet50": _resnet is not None, "triage_yolo11n_cls7": _yolo7 is not None}


def _vote(model_name: str, label: str | None, probs: dict, elapsed_ms: float, error: str | None = None) -> dict:
    return {
        "model_name": model_name,
        "predicted_class": label,
        "confidence": round(float(probs[label]), 6) if label else None,
        "confidence_source": "softmax",
        "all_probabilities": {k: round(float(v), 6) for k, v in probs.items()},
        "is_available": label is not None,
        "error": error,
        "inference_ms": round(elapsed_ms, 2),
    }


def run_resnet(image_bytes: bytes) -> dict:
    if _resnet is None:
        return _vote(RESNET_NAME, None, {}, 0.0, "Model not loaded")
    try:
        import torch
        from PIL import Image

        start = time.perf_counter()
        img = Image.open(io.BytesIO(image_bytes)).convert("RGB")
        x = _resnet_tf(img).unsqueeze(0).to(_device)
        with torch.inference_mode():
            logits, jam_logit = _resnet(x)
            p = torch.softmax(logits.float(), dim=1)[0].cpu().tolist()
        probs = dict(zip(RESNET_LABELS, p))
        label = max(probs, key=probs.get)
        vote = _vote(RESNET_NAME, label, probs, (time.perf_counter() - start) * 1000)
        vote["jam_probability"] = round(float(torch.sigmoid(jam_logit.float())[0]), 6)
        return vote
    except Exception as e:
        logger.error(f"Triage ResNet-50 inference error: {e}")
        return _vote(RESNET_NAME, None, {}, 0.0, str(e))


def run_yolo7(image_bytes: bytes) -> dict:
    if _yolo7 is None:
        return _vote(YOLO7_NAME, None, {}, 0.0, "Model not loaded")
    try:
        from PIL import Image

        start = time.perf_counter()
        img = Image.open(io.BytesIO(image_bytes)).convert("RGB")
        result = _yolo7(img, verbose=False)[0]
        p = result.probs.data.float().cpu().tolist()
        probs = {str(_yolo7.names[i]): p[i] for i in range(len(p))}
        label = max(probs, key=probs.get)
        return _vote(YOLO7_NAME, label, probs, (time.perf_counter() - start) * 1000)
    except Exception as e:
        logger.error(f"Triage YOLO11n-cls inference error: {e}")
        return _vote(YOLO7_NAME, None, {}, 0.0, str(e))

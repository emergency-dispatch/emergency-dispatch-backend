# AI service — TriageVL-3B

Microservice FastAPI phục vụ mô hình **TriageVL-3B**: Qwen2.5-VL-3B (4-bit) kèm adapter LoRA của nhóm.
Backend gọi `POST /api/analyze-incident` qua cấu hình `AiService:LocalServiceUrl`. Nếu service lỗi hoặc quá thời gian chờ, backend chuyển sang OpenRouter, rồi tới trạng thái Unclassified.

```
ai-service/
├── ai_service.py          # FastAPI service
├── requirements.txt       # phiên bản thư viện đã chạy được
├── run_ai_service.bat     # chạy local trên Windows (port 8000)
├── kaggle_serve_ngrok.py  # chạy trên Kaggle + ngrok (dán vào 1 cell)
└── lora_adapter/          # KHÔNG có trong git (~127 MB) — tự chép vào đây
```

## Adapter

`lora_adapter/` (gồm `adapter_model.safetensors`, `adapter_config.json`, tokenizer…) không đưa lên git vì quá lớn.
Bản gốc nằm ở `D:\SEP490\TrainAI\lora_adapter`. Chép cả thư mục vào `ai-service/lora_adapter/`, hoặc đặt biến môi trường `AI_LORA_ADAPTER_DIR` trỏ tới nơi khác.

## Chạy local (GPU ≥ 4 GB)

```bash
pip install torch --index-url https://download.pytorch.org/whl/cu126
pip install -r requirements.txt
run_ai_service.bat            # hoặc: python ai_service.py
```

Kiểm tra: `GET http://127.0.0.1:8000/health` → `"model_ready": true`. Backend để `AiService:ApiKey` trống.

## Chạy trên Kaggle (ngrok)

Làm theo hướng dẫn ở đầu `kaggle_serve_ngrok.py`. Notebook in ra sẵn đoạn `AiService` để dán vào `appsettings.json` của backend.

## Biến môi trường

| Biến | Mặc định | Ý nghĩa |
|---|---|---|
| `AI_LORA_ADAPTER_DIR` | `./lora_adapter` | Thư mục adapter |
| `AI_SERVICE_API_KEY` | trống (không kiểm tra) | Bắt buộc header `X-AI-Service-Key` đúng khóa. **Phải đặt khi public** |
| `AI_MAX_IMAGE_SIDE` | `384` | Cạnh ảnh tối đa. 384 cho GPU 4 GB; 512 giống lúc đánh giá |
| `AI_ALLOW_LOCAL_PATHS` | `1` | Cho phép `image_url` là đường dẫn file trên máy. **Đặt `0` khi public** |

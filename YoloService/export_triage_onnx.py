#!/usr/bin/env python3
"""
Xuất 2 model CNN 7 mức (ResNet-50, YOLO11n-cls) sang ONNX để backend .NET chạy trực tiếp bằng ONNX Runtime
(chế độ "3 AI bỏ phiếu" không cần YoloService / PyTorch, chạy được trên Render free 512 MB).

    python export_triage_onnx.py                      # đọc models/*.pt, ghi ../EmergencyDispatch.API/AiModels/
    python export_triage_onnx.py --out D:/tmp/onnx    # ghi chỗ khác

Đầu ra:
  triage_resnet50.onnx      input "image" [1,3,512,512] (RGB, chuẩn hoá ImageNet), output "logits" [1,7]
                            theo thứ tự fire_L1, fire_L2, fire_L3, accident_L1, accident_L2, accident_L3, none
  triage_yolo11n_cls7.onnx  input "images" [1,3,224,224] (RGB / 255), output xác suất [1,7];
                            thứ tự lớp nằm trong metadata "names" (ultralytics ghi sẵn)
Train lại model → chạy lại script này rồi deploy lại backend.
"""
import argparse
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(HERE, "..", "EmergencyDispatch.API", "AiModels"))
    args = ap.parse_args()
    out = os.path.abspath(args.out)
    os.makedirs(out, exist_ok=True)

    import torch
    from torch import nn
    import triage_models as tm

    tm.load_triage_models("cpu")
    if tm._resnet is None or tm._yolo7 is None:
        sys.exit("Thiếu models/triage_resnet50.pt hoặc models/triage_yolo11n_cls7.pt")

    class SeverityOnly(nn.Module):
        """Chỉ xuất đầu 7 mức (đầu kẹt xe không dùng cho bỏ phiếu)."""

        def __init__(self, m):
            super().__init__()
            self.m = m

        def forward(self, x):
            return self.m(x)[0]

    resnet_path = os.path.join(out, "triage_resnet50.onnx")
    torch.onnx.export(SeverityOnly(tm._resnet).eval(), torch.randn(1, 3, tm.RESNET_IMAGE_SIZE, tm.RESNET_IMAGE_SIZE),
                      resnet_path, input_names=["image"], output_names=["logits"], opset_version=17, dynamo=False)

    exported = tm._yolo7.export(format="onnx", imgsz=224, opset=17, simplify=False, dynamic=False)
    yolo_path = os.path.join(out, "triage_yolo11n_cls7.onnx")
    shutil.move(exported, yolo_path)

    import onnxruntime as ort
    import numpy as np

    r = ort.InferenceSession(resnet_path, providers=["CPUExecutionProvider"])
    y = ort.InferenceSession(yolo_path, providers=["CPUExecutionProvider"])
    assert r.run(None, {"image": np.zeros((1, 3, 512, 512), np.float32)})[0].shape == (1, 7)
    assert y.run(None, {y.get_inputs()[0].name: np.zeros((1, 3, 224, 224), np.float32)})[0].shape == (1, 7)
    print("names:", y.get_modelmeta().custom_metadata_map.get("names"))
    for p in (resnet_path, yolo_path):
        print(f"{p}  {os.path.getsize(p) / 2**20:.1f} MB")


if __name__ == "__main__":
    main()

@echo off
chcp 65001 > nul
echo ====================================================================
echo  SEP490 EMERGENCY DISPATCH - LOCAL AI SERVICE (QWEN2.5-VL-3B LoRA)
echo ====================================================================
cd /d "%~dp0"
if not exist "lora_adapter\adapter_config.json" (
    echo [!] Chua co thu muc lora_adapter\ canh file nay.
    echo     Chep thu muc lora_adapter cua nhom vao: %~dp0lora_adapter
    pause
    exit /b 1
)
echo Dang khoi dong AI service tren GPU (port 8000)...
python ai_service.py
pause

namespace EmergencyDispatch.Application.Localization;

public static partial class LocalizationCatalog
{
    private static readonly Dictionary<string, string> AiViToEn = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Phân tích hiện trường bằng AI hoàn tất thành công."] = "AI scene analysis completed successfully.",
        ["AI gặp sự cố kết nối hoặc lỗi xử lý, hệ thống đã kích hoạt cơ chế Fallback an toàn (Mức độ: Unclassified)."] = "AI encountered a connection or processing error; a safe fallback was activated (severity: Unclassified).",
        ["Ảnh / video phải được tải lên qua hệ thống (POST /api/media/upload)."] = "Images / videos must be uploaded through the system (POST /api/media/upload).",
        ["AI gặp lỗi trong quá trình suy luận, không thể đánh giá kẹt xe."] = "AI inference failed; traffic congestion could not be assessed.",
        ["Không thể phân loại tự động bởi AI. Sự cố đã được gắn cờ để Điều phối viên (Operator) đánh giá thủ công."] = "Automatic AI classification is unavailable. The incident has been flagged for manual operator review.",
        ["Cả hai engine AI đều không khả dụng."] = "Both AI engines are unavailable.",
        ["Qwen-VL nhận định có dấu hiệu cản trở hoặc ùn tắc giao thông tại hiện trường."] = "Qwen-VL identified signs of traffic obstruction or congestion at the scene.",
        ["Qwen-VL không phát hiện dấu hiệu ùn tắc trên ảnh."] = "Qwen-VL identified no signs of traffic congestion in the image.",
        ["Qwen-VL không trả về trường đánh giá ùn tắc giao thông (thiếu dữ liệu)."] = "Qwen-VL did not return a traffic congestion assessment (missing data).",
        ["Qwen-VL Local nhận định có dấu hiệu cản trở hoặc ùn tắc giao thông."] = "Local Qwen-VL identified signs of traffic obstruction or congestion.",
        ["Qwen-VL Local không phát hiện dấu hiệu ùn tắc trên ảnh."] = "Local Qwen-VL identified no signs of traffic congestion in the image.",
        ["Qwen-VL Local không trả về trường đánh giá ùn tắc (thiếu dữ liệu)."] = "Local Qwen-VL did not return a traffic congestion assessment (missing data).",
        ["ĐỒNG THUẬN (Severity do Qwen-VL đánh giá)"] = "AGREEMENT (severity assessed by Qwen-VL)",
        ["BẤT ĐỒNG (YOLO phát hiện sự cố, Qwen-VL không) → Cần Điều phối viên thẩm định (Severity=0/Unclassified)"] = "DISAGREEMENT (YOLO identified an incident; Qwen-VL did not) → Operator review required (Severity=0/Unclassified)",
        ["Qwen-VL phát hiện sự cố, YOLO reject → Tin Qwen-VL reasoning (Severity do Qwen-VL đánh giá)"] = "Qwen-VL identified an incident; YOLO rejected it → Using Qwen-VL assessment (severity assessed by Qwen-VL)",
        ["BẤT ĐỒNG LOẠI SỰ CỐ (YOLO={0}, Qwen={1}) → Ưu tiên Qwen-VL reasoning, cần Điều phối viên thẩm định"] = "INCIDENT TYPE DISAGREEMENT (YOLO={0}, Qwen={1}) → Using Qwen-VL assessment; operator review required",
        ["Tình trạng giao thông"] = "Traffic assessment",
        ["YOLO không có chi tiết đếm xe"] = "YOLO vehicle-count details are unavailable",
        ["Qwen-VL không có chi tiết ngữ cảnh"] = "Qwen-VL contextual details are unavailable",
        ["Nghi ngờ — Cả hai AI đồng thuận ghi nhận dấu hiệu ùn tắc. (YOLO: {0} | Qwen: {1})"] = "Suspected — Both AI engines identified signs of congestion. (YOLO: {0} | Qwen: {1})",
        ["Bất đồng — YOLO ghi nhận đông xe nhưng Qwen-VL không phát hiện ùn tắc. (YOLO: {0} | Qwen: {1})"] = "Disputed — YOLO identified dense traffic, while Qwen-VL identified no congestion. (YOLO: {0} | Qwen: {1})",
        ["Bất đồng — Qwen-VL nhận định ùn tắc nhưng YOLO đếm dưới ngưỡng. (Qwen: {0} | YOLO: {1})"] = "Disputed — Qwen-VL identified congestion, while YOLO did not identify sufficient spatial evidence. (Qwen: {0} | YOLO: {1})",
        ["Cả hai mô hình AI đều chưa phát hiện dấu hiệu ùn tắc trên ảnh."] = "Neither AI engine identified signs of congestion in the image.",
        ["Nghi ngờ — Chỉ YOLO ghi nhận đông xe ({0}); Qwen-VL gặp sự cố, thiếu nguồn đối chiếu."] = "Suspected — Only YOLO identified dense traffic ({0}); Qwen-VL is unavailable, so corroborating evidence is missing.",
        ["Nghi ngờ — Chỉ Qwen-VL ghi nhận ùn tắc ({0}); YOLO gặp sự cố, thiếu nguồn đối chiếu."] = "Suspected — Only Qwen-VL identified congestion ({0}); YOLO is unavailable, so corroborating evidence is missing.",
        ["Chưa xác minh — YOLO không thấy dấu hiệu ({0}) nhưng Qwen-VL gặp sự cố, thiếu nguồn độc lập đối chiếu."] = "Unverified — YOLO identified no signs ({0}), but Qwen-VL is unavailable, so independent corroboration is missing.",
        ["Chưa xác minh — Qwen-VL không thấy dấu hiệu ({0}) nhưng YOLO gặp sự cố, thiếu nguồn độc lập đối chiếu."] = "Unverified — Qwen-VL identified no signs ({0}), but YOLO is unavailable, so independent corroboration is missing.",
        ["Chưa thể đánh giá ùn tắc: Cả hai mô hình AI đều không khả dụng hoặc gặp lỗi."] = "Traffic congestion could not be assessed: both AI engines are unavailable or failed.",
        ["Qwen-VL trả loại sự cố ngoài fire/accident/none."] = "Qwen-VL returned an incident type outside fire/accident/none.",
        ["Qwen-VL thiếu mức độ nghiêm trọng nguyên từ 0 đến 3."] = "Qwen-VL did not return an integer severity between 0 and 3.",
        ["Qwen-VL thiếu nội dung đánh giá hiện trường."] = "Qwen-VL did not return a scene assessment.",
        ["Qwen-VL trả hazardTags không phải danh sách."] = "Qwen-VL returned hazardTags as a non-array value.",
        ["Qwen-VL trả hazardTags chứa phần tử không phải chuỗi."] = "Qwen-VL returned a non-string item in hazardTags.",
        ["Qwen-VL trả độ tin cậy ngoài khoảng 0 đến 1."] = "Qwen-VL returned a confidence value outside 0 to 1.",
        ["OpenRouter API Key chưa được cấu hình. Cần cài đặt API key hợp lệ trong appsettings.json → OpenRouter:ApiKey."] = "The OpenRouter API key is not configured. Configure a valid OpenRouter:ApiKey.",
    };
}

namespace EmergencyDispatch.Application.Localization;

public static partial class LocalizationCatalog
{
    private static readonly Dictionary<string, string> AudioViToEn = new(StringComparer.Ordinal)
    {
        { "Bản ghi âm không hợp lệ, đã hết hạn hoặc không thuộc người gửi. Vui lòng tải lại bản ghi.", "The recording is invalid, has expired, or belongs to another sender. Please upload it again." },
        { "Mỗi lần gửi chỉ được đính kèm một bản ghi âm và tối đa 5 tệp tin tổng cộng.", "Each submission allows one recording and up to 5 attachments in total." },
        { "Bản chép lời không được vượt quá 4000 ký tự.", "The transcript must not exceed 4000 characters." },
        { "AI không tự điều phối: bản ghi âm chưa có bản chép lời sử dụng được. Cần Điều phối viên nghe và xác nhận.", "AI did not dispatch automatically: the recording has no usable transcript. An operator must listen and verify." },
        { "Bản ghi âm không được vượt quá 5MB.", "The recording must not exceed 5MB." },
        { "Định dạng tệp '{0}' không được hỗ trợ. Hệ thống chỉ chấp nhận ảnh, video hoặc âm thanh được hỗ trợ.", "File type '{0}' is not supported. Only supported images, videos, or audio files are accepted." },
        { "Chưa cấu hình lưu trữ âm thanh. Vui lòng gửi SOS bằng ảnh hoặc ghi chú.", "Audio storage is not configured. Please send the SOS with a photo or a note." },
        { "Không thể lưu bản ghi âm. Vui lòng thử lại hoặc gửi SOS không kèm âm thanh.", "The recording could not be saved. Please retry or send the SOS without audio." },
        { "Bản ghi phải là tệp âm thanh hợp lệ, có thời lượng từ trên 0 đến 30 giây.", "The recording must be a valid audio-only file lasting more than 0 and no more than 30 seconds." },
        { "Không thể xác nhận bản ghi âm. Vui lòng thử lại.", "The recording could not be verified. Please try again." },
        { "Không thể mở bản ghi âm.", "The recording could not be opened." },
        { "Không tìm thấy bản ghi âm.", "Recording not found." },
        { "bản ghi âm mới", "new audio recording" },
    };
}

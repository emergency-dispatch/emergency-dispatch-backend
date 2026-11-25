using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Media;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Application.Services;

public static class AudioEvidence
{
    public const string InvalidAttachment = "Bản ghi âm không hợp lệ, đã hết hạn hoặc không thuộc người gửi. Vui lòng tải lại bản ghi.";
    public const string TooManyAttachments = "Mỗi lần gửi chỉ được đính kèm một bản ghi âm và tối đa 5 tệp tin tổng cộng.";
    public const string MissingDescriptionReview = "AI không tự điều phối: bản ghi âm chưa có mô tả hiện trường. Cần Điều phối viên nghe và xác nhận.";

    public static bool TryResolve(IReadOnlyList<AudioAttachmentInputDto>? inputs, int otherMediaCount,
        IAudioUploadTokenService? tokens, Guid? userId, out List<IncidentMedia> media, out string? error)
    {
        media = new(); error = null;
        if ((inputs?.Count ?? 0) > 1 || otherMediaCount + (inputs?.Count ?? 0) > 5)
        { error = TooManyAttachments; return false; }
        foreach (var input in inputs ?? Array.Empty<AudioAttachmentInputDto>())
        {
            if (input == null || tokens == null || !tokens.TryRead(input.UploadToken, userId, out var receipt)
                || receipt?.Media is not { MediaType: MediaType.Audio, DurationSeconds: > 0 and <= 30 })
            { error = InvalidAttachment; return false; }
            var saved = receipt.Media;
            media.Add(new IncidentMedia
            {
                MediaUrl = saved.MediaUrl, PublicId = saved.PublicId, MediaType = MediaType.Audio,
                MimeType = saved.MimeType, FileSizeBytes = saved.FileSizeBytes,
                DurationSeconds = saved.DurationSeconds
            });
        }
        return true;
    }

    public static IncidentMediaDto ToDto(IncidentMedia media) => new()
    {
        Id = media.Id, MediaUrl = media.MediaUrl, PublicId = media.PublicId,
        MediaType = media.MediaType, FileSizeBytes = media.FileSizeBytes, MimeType = media.MimeType,
        DurationSeconds = media.DurationSeconds
    };

    // Legacy URLs have no metadata. Audio requires the signed upload receipt, especially for ambiguous WebM containers.
    public static MediaType LegacyType(string url)
    {
        var path = Uri.UnescapeDataString(Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (path.Contains("/emergency_dispatch/audio/", StringComparison.OrdinalIgnoreCase)
            || ext is ".m4a" or ".mp3" or ".aac" or ".wav" or ".ogg" or ".opus") return MediaType.Audio;
        return ext is ".mp4" or ".mov" or ".webm" ? MediaType.Video : MediaType.Photo;
    }
}

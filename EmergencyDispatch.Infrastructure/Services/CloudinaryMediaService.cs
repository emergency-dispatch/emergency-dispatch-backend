using EmergencyDispatch.Application.Localization;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using EmergencyDispatch.Application.DTOs.Media;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

public class CloudinaryMediaService : IMediaUploadService, IAudioPlaybackService
{
    private readonly ICloudinary? _cloudinary;
    private readonly ILogger<CloudinaryMediaService> _logger;
    private readonly bool _isConfigured;

    public CloudinaryMediaService(IConfiguration configuration, ILogger<CloudinaryMediaService> logger, ICloudinary? cloudinary = null)
    {
        _logger = logger;
        // Cùng điều kiện với MediaUrlPolicy: thiếu một khóa → chế độ mock, validator vẫn nhận URL mock
        if (EmergencyDispatch.Application.Validators.MediaUrlPolicy.IsCloudinaryConfigured(configuration))
        {
            var account = new Account(
                configuration["Cloudinary:CloudName"]!.Trim(),
                configuration["Cloudinary:ApiKey"]!.Trim(),
                configuration["Cloudinary:ApiSecret"]!.Trim());
            _cloudinary = cloudinary ?? new Cloudinary(account) { Api = { Secure = true } };
            _isConfigured = true;
        }
        else
        {
            _logger.LogWarning("Cloudinary chưa được cấu hình với API Key thật. Các tệp tin tải lên sẽ tạo Mock URL cục bộ.");
            _isConfigured = false;
        }
    }

    public async Task<MediaUploadResultDto> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        contentType = EmergencyDispatch.Application.Validators.MediaValidationHelper.NormalizeMimeType(contentType);
        if (EmergencyDispatch.Application.Validators.MediaValidationHelper.IsAudio(contentType))
            return await UploadAudioAsync(stream, fileName, contentType, cancellationToken);
        var isVideo = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        var mediaType = isVideo ? MediaType.Video : MediaType.Photo;

        if (!_isConfigured || _cloudinary == null)
        {
            // Mock URL phục vụ dev/test khi chưa cấu hình Cloudinary
            var mockId = Guid.NewGuid().ToString("N");
            var ext = Path.GetExtension(fileName);
            var mockUrl = $"https://images.unsplash.com/photo-1542382257-80dedb725088?w=800&auto=format&fit=crop&q=60"; // Hình đám cháy/cứu hộ mẫu
            return new MediaUploadResultDto
            {
                Url = mockUrl,
                PublicId = $"mock_{mockId}",
                MediaType = mediaType,
                FileSizeBytes = stream.Length,
                MimeType = contentType,
                IsSuccess = true
            };
        }

        try
        {
            if (isVideo)
            {
                var uploadParams = new VideoUploadParams
                {
                    File = new FileDescription(fileName, stream),
                    Folder = "emergency_dispatch/videos"
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams, cancellationToken);
                return new MediaUploadResultDto
                {
                    Url = uploadResult.SecureUrl?.AbsoluteUri ?? uploadResult.Url?.AbsoluteUri ?? string.Empty,
                    PublicId = uploadResult.PublicId,
                    MediaType = MediaType.Video,
                    FileSizeBytes = uploadResult.Bytes,
                    MimeType = contentType,
                    IsSuccess = uploadResult.Error == null,
                    ErrorMessage = uploadResult.Error?.Message
                };
            }
            else
            {
                var uploadParams = new ImageUploadParams
                {
                    File = new FileDescription(fileName, stream),
                    Folder = "emergency_dispatch/images",
                    Transformation = new Transformation().Quality("auto").FetchFormat("auto")
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams, cancellationToken);
                return new MediaUploadResultDto
                {
                    Url = uploadResult.SecureUrl?.AbsoluteUri ?? uploadResult.Url?.AbsoluteUri ?? string.Empty,
                    PublicId = uploadResult.PublicId,
                    MediaType = MediaType.Photo,
                    FileSizeBytes = uploadResult.Bytes,
                    MimeType = contentType,
                    IsSuccess = uploadResult.Error == null,
                    ErrorMessage = uploadResult.Error?.Message
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi tải tệp tin lên Cloudinary: {FileName}", fileName);
            return new MediaUploadResultDto
            {
                IsSuccess = false,
                ErrorMessage = LocalizationHelper.Localize("Lỗi upload Cloudinary: {0}", ex.Message),
                MediaType = mediaType,
                MimeType = contentType
            };
        }
    }

    private async Task<MediaUploadResultDto> UploadAudioAsync(Stream stream, string fileName, string contentType, CancellationToken ct)
    {
        if (stream.CanSeek)
        {
            var validation = EmergencyDispatch.Application.Validators.MediaValidationHelper.ValidateMedia(contentType, stream.Length);
            if (!validation.IsValid) return new MediaUploadResultDto { IsSuccess = false, MediaType = MediaType.Audio, ErrorMessage = validation.ErrorMessage };
        }
        if (!_isConfigured || _cloudinary == null)
            return AudioFailure("Chưa cấu hình lưu trữ âm thanh. Vui lòng gửi SOS bằng ảnh hoặc ghi chú.");
        try
        {
            var upload = await _cloudinary.UploadAsync(new VideoUploadParams
            {
                File = new FileDescription(fileName, stream), Folder = "emergency_dispatch/audio", Type = "authenticated"
            }, ct);
            if (upload.Error != null || string.IsNullOrWhiteSpace(upload.PublicId) || upload.SecureUrl == null && upload.Url == null)
                return AudioFailure("Không thể lưu bản ghi âm. Vui lòng thử lại hoặc gửi SOS không kèm âm thanh.");
            var duration = upload.Duration;
            // The SDK creates an empty Video object even for audio-only assets; inspect its codec, not nullability.
            if (string.IsNullOrWhiteSpace(upload.Audio?.Codec) || !string.IsNullOrWhiteSpace(upload.Video?.Codec)
                || !double.IsFinite(duration) || duration <= 0 || duration > 30)
            {
                // A failed validation must not leave a playable, over-limit recording behind.
                await _cloudinary.DestroyAsync(new DeletionParams(upload.PublicId) { ResourceType = ResourceType.Video, Type = "authenticated" });
                return AudioFailure("Bản ghi phải là tệp âm thanh hợp lệ, có thời lượng từ trên 0 đến 30 giây.");
            }
            // Authenticated uploads may return an already signed CDN URL. Persist only the unsigned asset URL;
            // playback is granted later through the permission-checked, short-lived download URL.
            var sourceUrl = upload.SecureUrl ?? upload.Url!;
            var assetUrl = new UriBuilder(sourceUrl)
            {
                Scheme = Uri.UriSchemeHttps, Port = -1, Query = string.Empty, Fragment = string.Empty,
                Path = System.Text.RegularExpressions.Regex.Replace(sourceUrl.AbsolutePath, @"/s--[A-Za-z0-9_-]+--/", "/")
            }.Uri.AbsoluteUri;
            return new MediaUploadResultDto
            {
                Url = assetUrl,
                PublicId = upload.PublicId, MediaType = MediaType.Audio, FileSizeBytes = upload.Bytes,
                MimeType = contentType, DurationSeconds = duration, TranscriptionStatus = "AwaitingClientTranscript"
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio upload failed.");
            return AudioFailure("Không thể lưu bản ghi âm. Vui lòng thử lại hoặc gửi SOS không kèm âm thanh.");
        }
    }

    private static MediaUploadResultDto AudioFailure(string message) => new()
    {
        IsSuccess = false, MediaType = MediaType.Audio, ErrorMessage = LocalizationHelper.Localize(message)
    };

    public AudioPlaybackUrlDto? CreatePlaybackUrl(EmergencyDispatch.Application.DTOs.Incident.IncidentMediaDto media)
    {
        if (_cloudinary == null || media.MediaType != MediaType.Audio || string.IsNullOrWhiteSpace(media.PublicId)
            || !media.PublicId.StartsWith("emergency_dispatch/audio/", StringComparison.Ordinal)) return null;
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        var format = Uri.TryCreate(media.MediaUrl, UriKind.Absolute, out var uri) ? Path.GetExtension(uri.AbsolutePath).TrimStart('.') : null;
        var url = _cloudinary.DownloadPrivate(media.PublicId, attachment: false, format: format,
            type: "authenticated", expiresAt: new DateTimeOffset(expiresAt).ToUnixTimeSeconds(), resourceType: "video");
        return new AudioPlaybackUrlDto(url, expiresAt);
    }

    public async Task<bool> DeleteAsync(string publicId, CancellationToken cancellationToken = default)
    {
        if (!_isConfigured || _cloudinary == null)
        {
            return true;
        }

        try
        {
            var delParams = new DeletionParams(publicId);
            if (publicId.StartsWith("emergency_dispatch/audio/", StringComparison.Ordinal))
            {
                delParams.ResourceType = ResourceType.Video;
                delParams.Type = "authenticated";
            }
            var result = await _cloudinary.DestroyAsync(delParams);
            return result.Result == "ok";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xóa media trên Cloudinary: {PublicId}", publicId);
            return false;
        }
    }
}

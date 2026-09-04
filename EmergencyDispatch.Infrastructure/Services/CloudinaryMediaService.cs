using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using EmergencyDispatch.Application.DTOs.Media;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

public class CloudinaryMediaService : IMediaUploadService
{
    private readonly Cloudinary? _cloudinary;
    private readonly ILogger<CloudinaryMediaService> _logger;
    private readonly bool _isConfigured;

    public CloudinaryMediaService(IConfiguration configuration, ILogger<CloudinaryMediaService> logger)
    {
        _logger = logger;
        var cloudName = configuration["Cloudinary:CloudName"];
        var apiKey = configuration["Cloudinary:ApiKey"];
        var apiSecret = configuration["Cloudinary:ApiSecret"];

        if (!string.IsNullOrWhiteSpace(cloudName) &&
            !cloudName.StartsWith("YOUR_") &&
            !string.IsNullOrWhiteSpace(apiKey) &&
            !apiKey.StartsWith("YOUR_") &&
            !string.IsNullOrWhiteSpace(apiSecret) &&
            !apiSecret.StartsWith("YOUR_"))
        {
            var account = new Account(cloudName, apiKey, apiSecret);
            _cloudinary = new Cloudinary(account) { Api = { Secure = true } };
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
                ErrorMessage = $"Lỗi upload Cloudinary: {ex.Message}",
                MediaType = mediaType,
                MimeType = contentType
            };
        }
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

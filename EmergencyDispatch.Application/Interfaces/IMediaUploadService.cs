using EmergencyDispatch.Application.DTOs.Media;

namespace EmergencyDispatch.Application.Interfaces;

/// <summary>
/// Dịch vụ tải tệp tin đa phương tiện lên Cloud Storage (Cloudinary)
/// </summary>
public interface IMediaUploadService
{
    /// <summary>
    /// Upload ảnh/video hoặc audio lên Cloudinary; audio được lưu authenticated và kiểm tra thời lượng.
    /// </summary>
    Task<MediaUploadResultDto> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Xóa tệp tin trên Cloudinary theo PublicId
    /// </summary>
    Task<bool> DeleteAsync(string publicId, CancellationToken cancellationToken = default);
}

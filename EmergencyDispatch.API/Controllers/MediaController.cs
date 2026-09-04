using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Media;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Validators;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// API tiếp nhận tải lên hình ảnh và video hiện trường
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly IMediaUploadService _mediaUploadService;

    public MediaController(IMediaUploadService mediaUploadService)
    {
        _mediaUploadService = mediaUploadService;
    }

    /// <summary>
    /// Tải lên một tệp tin hình ảnh (&lt;= 10MB) hoặc video (&lt;= 30MB) hiện trường
    /// </summary>
    /// <param name="file">Tệp tin hình ảnh (JPEG, PNG, WEBP) hoặc video (MP4, MOV)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>URL bảo mật và PublicId từ Cloudinary</returns>
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponseDto<MediaUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<MediaUploadResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadMedia([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponseDto<MediaUploadResultDto>.FailureResult("Vui lòng chọn tệp tin cần tải lên."));
        }

        // Bước 1: Input Validation Logic (Application Layer)
        var (isValid, errorMessage) = MediaValidationHelper.ValidateMedia(file.ContentType, file.Length);
        if (!isValid)
        {
            return BadRequest(ApiResponseDto<MediaUploadResultDto>.FailureResult(errorMessage!));
        }

        using var stream = file.OpenReadStream();
        var result = await _mediaUploadService.UploadAsync(stream, file.FileName, file.ContentType, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(ApiResponseDto<MediaUploadResultDto>.FailureResult(result.ErrorMessage ?? "Tải tệp tin thất bại."));
        }

        return Ok(ApiResponseDto<MediaUploadResultDto>.SuccessResult(result, "Tải tệp tin lên thành công."));
    }
}

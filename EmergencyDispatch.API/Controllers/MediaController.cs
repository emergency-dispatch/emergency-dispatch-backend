using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Media;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Validators;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EmergencyDispatch.API.Controllers;

/// <summary>
/// API tiếp nhận hình ảnh, video và bản ghi âm hiện trường
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly IMediaUploadService _mediaUploadService;
    private readonly IAudioUploadTokenService? _audioTokens;
    private readonly IAudioPlaybackService? _audioPlayback;
    private readonly ICurrentUserContext? _currentUser;

    public MediaController(IMediaUploadService mediaUploadService, IAudioUploadTokenService? audioTokens = null,
        IAudioPlaybackService? audioPlayback = null, ICurrentUserContext? currentUser = null)
    {
        _mediaUploadService = mediaUploadService;
        _audioTokens = audioTokens; _audioPlayback = audioPlayback; _currentUser = currentUser;
    }

    /// <summary>
    /// Tải ảnh (&lt;= 10MB), video (&lt;= 30MB) hoặc audio (&lt;= 5MB, tối đa 30 giây). Audio cần Cloudinary thật và trả uploadToken.
    /// </summary>
    /// <param name="file">Ảnh, video hoặc âm thanh (M4A/AAC, MP3, WebM/Opus, OGG, WAV)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>URL bảo mật và PublicId từ Cloudinary</returns>
    [HttpPost("upload")]
    [EnableRateLimiting("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponseDto<MediaUploadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponseDto<MediaUploadResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadMedia(IFormFile? file, CancellationToken cancellationToken)
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

        if (result.MediaType == EmergencyDispatch.Domain.Enums.MediaType.Audio)
        {
            if (_audioTokens == null || result.DurationSeconds is not > 0 or > 30 || string.IsNullOrWhiteSpace(result.PublicId))
                return BadRequest(ApiResponseDto<MediaUploadResultDto>.FailureResult("Không thể xác nhận bản ghi âm. Vui lòng thử lại."));
            result.UploadToken = _audioTokens.Create(new EmergencyDispatch.Application.DTOs.Incident.IncidentMediaDto
            {
                MediaUrl = result.Url, PublicId = result.PublicId, MediaType = result.MediaType,
                FileSizeBytes = result.FileSizeBytes, MimeType = result.MimeType, DurationSeconds = result.DurationSeconds,
                TranscriptionStatus = "AwaitingClientTranscript"
            }, _currentUser?.UserId);
        }
        return Ok(ApiResponseDto<MediaUploadResultDto>.SuccessResult(result, "Tải tệp tin lên thành công."));
    }

    /// <summary>Preview an unsubmitted audio recording using its 24-hour upload receipt.</summary>
    [HttpPost("audio/preview-url")]
    [EnableRateLimiting("upload")]
    public IActionResult AudioPreview([FromBody] AudioAttachmentInputDto input)
    {
        Response.Headers.CacheControl = "no-store";
        if (_audioTokens == null || !_audioTokens.TryRead(input.UploadToken, _currentUser?.UserId, out var receipt))
            return BadRequest(ApiResponseDto<AudioPlaybackUrlDto>.FailureResult(EmergencyDispatch.Application.Services.AudioEvidence.InvalidAttachment));
        var result = _audioPlayback?.CreatePlaybackUrl(receipt!.Media);
        return result == null ? BadRequest(ApiResponseDto<AudioPlaybackUrlDto>.FailureResult("Không thể mở bản ghi âm."))
            : Ok(ApiResponseDto<AudioPlaybackUrlDto>.SuccessResult(result));
    }
}

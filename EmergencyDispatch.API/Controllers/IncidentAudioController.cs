using System.Security.Claims;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Media;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmergencyDispatch.API.Controllers;

/// <summary>Audio access follows the private incident/supplement permissions, never the public citizen map.</summary>
[ApiController]
[Authorize]
[Route("api/incidents/{incidentId:guid}")]
public class IncidentAudioController(IIncidentService incidents, IIncidentFollowUpService supplements,
    IAudioPlaybackService playback) : ControllerBase
{
    private Guid? UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    [HttpGet("audio/{mediaId:guid}/playback-url")]
    public async Task<IActionResult> GetAudio(Guid incidentId, Guid mediaId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!UserId.HasValue) return Unauthorized();
        var incident = await incidents.GetIncidentByIdAsync(incidentId, UserId, cancellationToken);
        if (!incident.Success || incident.Data == null) return this.ToHttpResult(incident);
        var media = incident.Data.MediaItems.FirstOrDefault(m => m.Id == mediaId && m.MediaType == Domain.Enums.MediaType.Audio);
        var url = media == null ? null : playback.CreatePlaybackUrl(media);
        return url == null ? NotFound(ApiResponseDto<AudioPlaybackUrlDto>.NotFoundResult("Không tìm thấy bản ghi âm."))
            : Ok(ApiResponseDto<AudioPlaybackUrlDto>.SuccessResult(url));
    }

    [HttpGet("supplements/{supplementId:guid}/audio/{mediaId:guid}/playback-url")]
    public async Task<IActionResult> GetSupplementAudio(Guid incidentId, Guid supplementId, Guid mediaId, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!UserId.HasValue) return Unauthorized();
        var result = await supplements.GetSupplementsAsync(incidentId, UserId, cancellationToken);
        if (!result.Success || result.Data == null) return this.ToHttpResult(result);
        var media = result.Data.FirstOrDefault(s => s.Id == supplementId)?.AudioAttachments
            .FirstOrDefault(m => m.Id == mediaId && m.MediaType == Domain.Enums.MediaType.Audio);
        var url = media == null ? null : playback.CreatePlaybackUrl(media);
        return url == null ? NotFound(ApiResponseDto<AudioPlaybackUrlDto>.NotFoundResult("Không tìm thấy bản ghi âm."))
            : Ok(ApiResponseDto<AudioPlaybackUrlDto>.SuccessResult(url));
    }
}

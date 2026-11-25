using System.Security.Claims;
using System.Text.Json;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using EmergencyDispatch.API.Controllers;
using EmergencyDispatch.API.Services;
using EmergencyDispatch.Application.DTOs.Common;
using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.DTOs.Media;
using EmergencyDispatch.Application.DTOs.Settings;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Application.Validators;
using EmergencyDispatch.Domain.Entities;
using EmergencyDispatch.Domain.Enums;
using EmergencyDispatch.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;

namespace EmergencyDispatch.Tests.Application;

public class AudioEvidenceTests
{
    private readonly EphemeralDataProtectionProvider _provider = new();
    private static IncidentMediaDto Audio() => new()
    {
        MediaUrl = "https://res.cloudinary.com/demo/video/authenticated/v1/emergency_dispatch/audio/clip.webm",
        PublicId = "emergency_dispatch/audio/clip", MediaType = MediaType.Audio,
        DurationSeconds = 10, FileSizeBytes = 1024, MimeType = "audio/webm"
    };

    [Theory]
    [InlineData("audio/mp4")]
    [InlineData("audio/x-m4a")]
    [InlineData("audio/mpeg")]
    [InlineData("audio/aac")]
    [InlineData("audio/webm;codecs=opus")]
    [InlineData(" AUDIO/OGG ; codecs=opus")]
    [InlineData("audio/wav")]
    public void AudioMimeTypesAcceptFiveMiBAndRejectOversize(string mime)
    {
        Assert.True(MediaValidationHelper.ValidateMedia(mime, 5 * 1024 * 1024).IsValid);
        Assert.False(MediaValidationHelper.ValidateMedia(mime, 5 * 1024 * 1024 + 1).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EmptyOrInvalidLengthIsRejected(long length)
        => Assert.False(MediaValidationHelper.ValidateMedia("audio/mp4", length).IsValid);

    [Fact]
    public void SignedReceiptIsUserBoundAndTamperProof()
    {
        var tokens = new AudioUploadTokenService(_provider);
        var owner = Guid.NewGuid(); var token = tokens.Create(Audio(), owner);
        Assert.True(tokens.TryRead(token, owner, out var receipt));
        Assert.Equal("audio/webm", receipt!.Media.MimeType);
        Assert.False(tokens.TryRead(token, Guid.NewGuid(), out _));
        Assert.False(tokens.TryRead(token, null, out _));
        Assert.False(tokens.TryRead(token[..^12] + "tampered", owner, out _));
    }

    [Fact]
    public void ExpiredReceiptIsRejected()
    {
        var protector = _provider.CreateProtector("EmergencyDispatch.AudioUpload.v1").ToTimeLimitedDataProtector();
        var token = protector.Protect(System.Text.Json.JsonSerializer.Serialize(new AudioUploadReceipt(Audio(), null)), DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.False(new AudioUploadTokenService(_provider).TryRead(token, null, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30.01)]
    public void ReceiptWithInvalidDurationCannotBeUsed(double seconds)
    {
        var media = Audio(); media.DurationSeconds = seconds;
        var tokens = new AudioUploadTokenService(_provider);
        Assert.False(tokens.TryRead(tokens.Create(media, null), null, out _));
    }

    [Fact]
    public void AudioMetadataHasNoSeparateSceneTextField()
    {
        var tokens = new AudioUploadTokenService(_provider); var token = tokens.Create(Audio(), null);
        Assert.True(AudioEvidence.TryResolve(new[] { new AudioAttachmentInputDto { UploadToken = token } }, 1, tokens, null, out var media, out _));
        Assert.Equal(MediaType.Audio, media.Single().MediaType);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = System.Text.Json.JsonSerializer.Serialize(AudioEvidence.ToDto(media.Single()), options);
        Assert.DoesNotContain("transcript", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("transcriptionStatus", json, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<AudioAttachmentInputDto>(
            "{\"uploadToken\":\"token\",\"transcript\":\"old text\"}", options));
    }

    [Fact]
    public void LimitsAndInvalidReceiptsAreRejected()
    {
        var tokens = new AudioUploadTokenService(_provider); var token = tokens.Create(Audio(), null);
        var input = new AudioAttachmentInputDto { UploadToken = token };
        Assert.False(AudioEvidence.TryResolve(new[] { input, input }, 0, tokens, null, out _, out _));
        Assert.False(AudioEvidence.TryResolve(new[] { input }, 5, tokens, null, out _, out _));
        Assert.False(AudioEvidence.TryResolve(new[] { new AudioAttachmentInputDto { UploadToken = "bad" } }, 0, tokens, null, out _, out _));
    }

    [Theory]
    [InlineData("https://res.cloudinary.com/demo/video/authenticated/v1/emergency_dispatch/audio/c.webm")]
    [InlineData("https://res.cloudinary.com/demo/video/upload/c.mp3?x=1")]
    [InlineData("https://res.cloudinary.com/demo/video/authenticated/v1/%65mergency_dispatch/audio/c.webm")]
    public async Task AudioCannotBypassReceiptValidationThroughLegacyMediaUrls(string url)
    {
        var validator = new CreateIncidentDtoValidator(TestHelpers.Config(("Cloudinary:CloudName", "demo")));
        var dto = new CreateIncidentDto { Latitude = 10, Longitude = 106, LocationAddress = "Road", MediaUrls = new() { url } };
        Assert.False((await validator.ValidateAsync(dto)).IsValid);
    }

    [Theory]
    [InlineData(null, "audio-description-unavailable")]
    [InlineData("   ", "audio-description-unavailable")]
    [InlineData("Hai người bị thương", null)]
    public void AutomaticDispatchRequiresDescriptionWhenAudioIsAttached(string? description, string? reason)
    {
        var incident = new Incident
        {
            Status = IncidentStatus.AiProcessed, Severity = SeverityLevel.Level2, EventType = IncidentEventType.TrafficAccident,
            AiClassification = new() { IsSuccess = true, EventType = IncidentEventType.TrafficAccident, ConfidenceSource = "logprob", ConfidenceScore = .99 },
            Description = description ?? string.Empty, MediaItems = { new() { MediaType = MediaType.Audio } }
        };
        Assert.Equal(reason, AutoDispatchService.IneligibleReason(incident, new DispatchSettingsDto(), DateTime.UtcNow));
    }

    [Fact]
    public async Task AudioUploadDoesNotReturnFakeImageWhenCloudinaryIsMissing()
    {
        var service = new CloudinaryMediaService(TestHelpers.Config(), NullLogger<CloudinaryMediaService>.Instance);
        using var stream = new MemoryStream(new byte[10]);
        var result = await service.UploadAsync(stream, "clip.m4a", "audio/mp4");
        Assert.False(result.IsSuccess); Assert.Empty(result.Url); Assert.Equal(MediaType.Audio, result.MediaType);
    }

    private static VideoUploadResult Result(double duration, bool video = false)
        => JsonConvert.DeserializeObject<VideoUploadResult>("{\"public_id\":\"emergency_dispatch/audio/clip\",\"secure_url\":\"https://res.cloudinary.com/demo/video/authenticated/v1/emergency_dispatch/audio/clip.webm\",\"bytes\":1024,\"duration\":"
            + duration.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"audio\":{\"codec\":\"opus\"}" + (video ? ",\"video\":{\"codec\":\"vp9\"}" : "") + "}")!;

    [Theory]
    [InlineData(5, false, true)]
    [InlineData(30, false, true)]
    [InlineData(30.1, false, false)]
    [InlineData(0, false, false)]
    [InlineData(5, true, false)]
    public async Task CloudinaryVerifiesDecodedDurationAndRejectsVideoDisguisedAsAudio(double duration, bool hasVideo, bool expected)
    {
        var cloud = new Mock<ICloudinary>();
        cloud.Setup(c => c.UploadAsync(It.IsAny<VideoUploadParams>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result(duration, hasVideo));
        cloud.Setup(c => c.DestroyAsync(It.IsAny<DeletionParams>())).ReturnsAsync(new DeletionResult { Result = "ok" });
        var config = TestHelpers.Config(("Cloudinary:CloudName", "demo"), ("Cloudinary:ApiKey", "key"), ("Cloudinary:ApiSecret", "secret"));
        var service = new CloudinaryMediaService(config, NullLogger<CloudinaryMediaService>.Instance, cloud.Object);
        using var stream = new MemoryStream(new byte[10]);
        var result = await service.UploadAsync(stream, "clip.webm", "audio/webm;codecs=opus");
        Assert.Equal(expected, result.IsSuccess);
        cloud.Verify(c => c.UploadAsync(It.Is<VideoUploadParams>(p => p.Type == "authenticated" && p.Folder == "emergency_dispatch/audio"), It.IsAny<CancellationToken>()), Times.Once);
        cloud.Verify(c => c.DestroyAsync(It.Is<DeletionParams>(p => p.ResourceType == ResourceType.Video && p.Type == "authenticated")), expected ? Times.Never() : Times.Once());
        if (expected) { Assert.Equal(duration, result.DurationSeconds); Assert.Equal(MediaType.Audio, result.MediaType); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaybackDoesNotSignUrlsUnlessIncidentAccessIsGranted(bool allowed)
    {
        var user = Guid.NewGuid(); var incidentId = Guid.NewGuid(); var media = Audio();
        var incidents = new Mock<IIncidentService>(); var playback = new Mock<IAudioPlaybackService>();
        incidents.Setup(s => s.GetIncidentByIdAsync(incidentId, user, It.IsAny<CancellationToken>())).ReturnsAsync(allowed
            ? ApiResponseDto<IncidentResponseDto>.SuccessResult(new() { MediaItems = new() { media } })
            : ApiResponseDto<IncidentResponseDto>.ForbiddenResult("Forbidden"));
        playback.Setup(s => s.CreatePlaybackUrl(media)).Returns(new AudioPlaybackUrlDto("https://signed.example/audio", DateTime.UtcNow.AddMinutes(5)));
        var controller = new IncidentAudioController(incidents.Object, new Mock<IIncidentFollowUpService>().Object, playback.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.ToString()) }, "test")) } }
        };
        var result = await controller.GetAudio(incidentId, media.Id, default);
        Assert.Equal(allowed ? 200 : 403, ((ObjectResult)result).StatusCode);
        playback.Verify(s => s.CreatePlaybackUrl(It.IsAny<IncidentMediaDto>()), allowed ? Times.Once() : Times.Never());
        Assert.Equal("no-store", controller.Response.Headers.CacheControl);
    }
    [Fact]
    public async Task PlaybackRequiresAValidUserIdEvenIfTheRequestHasAnIdentity()
    {
        var incidents = new Mock<IIncidentService>(); var follow = new Mock<IIncidentFollowUpService>();
        var playback = new Mock<IAudioPlaybackService>();
        var controller = new IncidentAudioController(incidents.Object, follow.Object, playback.Object)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<UnauthorizedResult>(await controller.GetAudio(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.IsType<UnauthorizedResult>(await controller.GetSupplementAudio(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), default));
        incidents.VerifyNoOtherCalls(); follow.VerifyNoOtherCalls(); playback.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task IncidentGroupDoesNotReceivePrivateAudioOrSceneText()
    {
        var clients = new Mock<Microsoft.AspNetCore.SignalR.IHubClients>();
        var crew = new Mock<Microsoft.AspNetCore.SignalR.IClientProxy>();
        var operators = new Mock<Microsoft.AspNetCore.SignalR.IClientProxy>();
        var citizens = new Mock<Microsoft.AspNetCore.SignalR.IClientProxy>();
        var stationId = Guid.NewGuid(); var incidentId = Guid.NewGuid();
        clients.Setup(c => c.Users(It.IsAny<IReadOnlyList<string>>())).Returns(crew.Object);
        clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(operators.Object);
        clients.Setup(c => c.Group(EmergencyDispatch.API.Realtime.RealtimeContract.IncidentGroup(incidentId))).Returns(citizens.Object);
        foreach (var proxy in new[] { crew, operators, citizens })
            proxy.Setup(c => c.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var hub = new Mock<Microsoft.AspNetCore.SignalR.IHubContext<EmergencyDispatch.API.Hubs.IncidentHub>>();
        hub.SetupGet(h => h.Clients).Returns(clients.Object);
        var notifier = new EmergencyDispatch.API.Realtime.SignalRRealtimeNotifier(hub.Object,
            new Mock<Microsoft.AspNetCore.SignalR.IHubContext<EmergencyDispatch.API.Hubs.TrackingHub>>().Object,
            NullLogger<EmergencyDispatch.API.Realtime.SignalRRealtimeNotifier>.Instance);
        var dto = new IncidentSupplementDto { TargetIncidentId = incidentId, Note = "Private speech text", ReporterName = "Private name", AudioAttachments = new() { Audio() } };
        await notifier.IncidentSupplementAddedAsync(dto, stationId, Array.Empty<Guid>(), new[] { Guid.NewGuid() });
        foreach (var proxy in new[] { crew, operators }) proxy.Verify(p => p.SendCoreAsync("ReceiveIncidentSupplement",
            It.Is<object?[]>(args => ReferenceEquals(args[0], dto)), It.IsAny<CancellationToken>()), Times.Once);
        citizens.Verify(p => p.SendCoreAsync("ReceiveIncidentSupplement",
            It.Is<object?[]>(args => ((IncidentSupplementDto)args[0]!).AudioAttachments.Count == 0 && ((IncidentSupplementDto)args[0]!).Note == string.Empty && ((IncidentSupplementDto)args[0]!).ReporterName == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PrivateAudioDeletionUsesVideoResourceAndAuthenticatedDelivery()
    {
        var cloud = new Mock<ICloudinary>();
        cloud.Setup(c => c.DestroyAsync(It.IsAny<DeletionParams>())).ReturnsAsync(new DeletionResult { Result = "ok" });
        var config = TestHelpers.Config(("Cloudinary:CloudName", "demo"), ("Cloudinary:ApiKey", "key"), ("Cloudinary:ApiSecret", "secret"));
        var service = new CloudinaryMediaService(config, NullLogger<CloudinaryMediaService>.Instance, cloud.Object);
        Assert.True(await service.DeleteAsync("emergency_dispatch/audio/test"));
        cloud.Verify(c => c.DestroyAsync(It.Is<DeletionParams>(p => p.ResourceType == ResourceType.Video && p.Type == "authenticated")), Times.Once);
    }

    [Fact]
    public async Task OversizeAudioDoesNotCallCloudinary()
    {
        var cloud = new Mock<ICloudinary>();
        var config = TestHelpers.Config(("Cloudinary:CloudName", "demo"), ("Cloudinary:ApiKey", "key"), ("Cloudinary:ApiSecret", "secret"));
        var service = new CloudinaryMediaService(config, NullLogger<CloudinaryMediaService>.Instance, cloud.Object);
        using var stream = new MemoryStream(new byte[5 * 1024 * 1024 + 1]);
        Assert.False((await service.UploadAsync(stream, "large.wav", "audio/wav")).IsSuccess);
        cloud.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CloudinaryEmptyVideoMetadataDoesNotRejectAValidAudioRecording()
    {
        var cloud = new Mock<ICloudinary>();
        var actualSdkShape = Result(1); actualSdkShape.Video = new Video();
        cloud.Setup(c => c.UploadAsync(It.IsAny<VideoUploadParams>(), It.IsAny<CancellationToken>())).ReturnsAsync(actualSdkShape);
        var config = TestHelpers.Config(("Cloudinary:CloudName", "demo"), ("Cloudinary:ApiKey", "key"), ("Cloudinary:ApiSecret", "secret"));
        var service = new CloudinaryMediaService(config, NullLogger<CloudinaryMediaService>.Instance, cloud.Object);
        using var stream = new MemoryStream(new byte[10]);
        Assert.True((await service.UploadAsync(stream, "audio.webm", "audio/webm")).IsSuccess);
        cloud.Verify(c => c.DestroyAsync(It.IsAny<DeletionParams>()), Times.Never);
    }

    [Fact]
    public async Task UploadResponseDoesNotExposeCloudinaryPermanentSignedUrl()
    {
        var cloud = new Mock<ICloudinary>(); var upload = Result(1);
        upload.SecureUrl = new Uri("https://res.cloudinary.com/demo/video/authenticated/s--abc_123--/v1/emergency_dispatch/audio/clip.webm?token=secret");
        cloud.Setup(c => c.UploadAsync(It.IsAny<VideoUploadParams>(), It.IsAny<CancellationToken>())).ReturnsAsync(upload);
        var config = TestHelpers.Config(("Cloudinary:CloudName", "demo"), ("Cloudinary:ApiKey", "key"), ("Cloudinary:ApiSecret", "secret"));
        var service = new CloudinaryMediaService(config, NullLogger<CloudinaryMediaService>.Instance, cloud.Object);
        using var stream = new MemoryStream(new byte[10]);
        var result = await service.UploadAsync(stream, "clip.webm", "audio/webm");
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("/s--", result.Url); Assert.Empty(new Uri(result.Url).Query);
        Assert.Contains("/video/authenticated/", result.Url);
    }

    [Theory]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public async Task SingleDescriptionSupportsFullSpeechTextAndEnforcesTheNewLimit(int length, bool expected)
    {
        var dto = new CreateIncidentDto { Latitude = 10, Longitude = 106, LocationAddress = "Road", Description = new string('a', length) };
        var validator = new CreateIncidentDtoValidator(TestHelpers.Config());
        Assert.Equal(expected, (await validator.ValidateAsync(dto)).IsValid);
    }

}

using EmergencyDispatch.Application.DTOs.Incident;
using EmergencyDispatch.Application.Validators;
using EmergencyDispatch.Domain.Enums;

namespace EmergencyDispatch.Tests.Application;

public class MediaUrlPolicyTests
{
    [Theory]
    [InlineData("https://res.cloudinary.com/demo/image/upload/v1/a.jpg", true)]
    [InlineData("https://res.cloudinary.com/other/image/upload/v1/a.jpg", false)]
    [InlineData("http://res.cloudinary.com/demo/image/upload/v1/a.jpg", false)]
    [InlineData("https://evil.example.com/demo/a.jpg", false)]
    [InlineData("https://images.unsplash.com/photo-1", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void IsTrusted_WithCloudinaryConfigured(string? url, bool expected)
    {
        var cfg = TestHelpers.Config(("Cloudinary:CloudName", "demo"));

        Assert.Equal(expected, MediaUrlPolicy.IsTrusted(url, cfg));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("YOUR_CLOUD_NAME")]
    public void IsTrusted_WithoutCloudinary_OnlyAcceptsMockHost(string? cloudName)
    {
        var cfg = TestHelpers.Config(("Cloudinary:CloudName", cloudName));

        Assert.True(MediaUrlPolicy.IsTrusted("https://images.unsplash.com/photo-1", cfg));
        Assert.False(MediaUrlPolicy.IsTrusted("https://res.cloudinary.com/x/a.jpg", cfg));
    }
}

public class MediaValidationHelperTests
{
    private const long Mb = 1024 * 1024;

    [Theory]
    [InlineData("image/jpeg", 10 * Mb)]
    [InlineData("IMAGE/PNG", 1)]
    [InlineData("image/webp", 5 * Mb)]
    [InlineData("video/mp4", 30 * Mb)]
    [InlineData("video/quicktime", 1)]
    public void ValidateMedia_AllowedTypesWithinLimit(string type, long size)
    {
        var (ok, error) = MediaValidationHelper.ValidateMedia(type, size);

        Assert.True(ok);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("image/jpeg", 10 * Mb + 1)]
    [InlineData("video/mp4", 30 * Mb + 1)]
    public void ValidateMedia_TooLarge_IsRejected(string type, long size)
    {
        var (ok, error) = TestHelpers.WithCulture("vi-VN", () => MediaValidationHelper.ValidateMedia(type, size));

        Assert.False(ok);
        Assert.Contains("vượt quá giới hạn", error);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/gif")]
    [InlineData("")]
    [InlineData(" ")]
    public void ValidateMedia_UnsupportedType_IsRejected(string type)
    {
        var (ok, error) = MediaValidationHelper.ValidateMedia(type, 1);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
    }
}

public class CreateIncidentDtoValidatorTests
{
    private readonly CreateIncidentDtoValidator _v = new(TestHelpers.Config(("Cloudinary:CloudName", "demo")));

    private static CreateIncidentDto Valid() => new()
    {
        Description = "Cháy nhà",
        Latitude = 10.77,
        Longitude = 106.70,
        LocationAddress = "1 Lê Lợi, Q1",
        ReporterPhone = "0901234567",
        MediaUrls = new() { "https://res.cloudinary.com/demo/image/upload/a.jpg" }
    };

    [Fact]
    public void ValidDto_Passes()
    {
        Assert.True(_v.Validate(Valid()).IsValid);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void CoordinatesOutOfRange_Fail(double lat, double lng)
    {
        var d = Valid();
        d.Latitude = lat;
        d.Longitude = lng;

        Assert.False(_v.Validate(d).IsValid);
    }

    [Fact]
    public void MissingAddress_Fails()
    {
        var d = Valid();
        d.LocationAddress = "";

        Assert.Contains(_v.Validate(d).Errors, e => e.PropertyName == nameof(CreateIncidentDto.LocationAddress));
    }

    [Fact]
    public void DescriptionOver200Chars_Fails()
    {
        var d = Valid();
        d.Description = new string('a', 201);

        Assert.Contains(_v.Validate(d).Errors, e => e.PropertyName == nameof(CreateIncidentDto.Description));
    }

    [Theory]
    [InlineData("0901234567", true)]
    [InlineData("+84901234567", true)]
    [InlineData("0381234567", true)]
    [InlineData("0201234567", false)]
    [InlineData("090123456", false)]
    [InlineData("abc", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    public void ReporterPhone_MustBeVietnameseMobileWhenGiven(string? phone, bool valid)
    {
        var d = Valid();
        d.ReporterPhone = phone;

        Assert.Equal(valid, _v.Validate(d).IsValid);
    }

    [Fact]
    public void MoreThanFiveMedia_Fails()
    {
        var d = Valid();
        d.MediaUrls = Enumerable.Range(0, 6).Select(i => $"https://res.cloudinary.com/demo/image/upload/{i}.jpg").ToList();

        Assert.False(_v.Validate(d).IsValid);
    }

    [Fact]
    public void UntrustedMediaUrl_Fails()
    {
        var d = Valid();
        d.MediaUrls = new() { "https://evil.example.com/a.jpg" };

        Assert.False(_v.Validate(d).IsValid);
    }
}

public class CreateOperatorIncidentDtoValidatorTests
{
    private readonly CreateOperatorIncidentDtoValidator _v = new(TestHelpers.Config(("Cloudinary:CloudName", "demo")));

    private static CreateOperatorIncidentDto Valid() => new()
    {
        Latitude = 10.77,
        Longitude = 106.70,
        LocationAddress = "1 Lê Lợi",
        Source = IncidentSource.PhoneCall,
        EventType = IncidentEventType.FireExplosion,
        Severity = SeverityLevel.Level2,
        OperatorNotes = "Người dân gọi báo cháy"
    };

    [Fact]
    public void ValidDto_Passes()
    {
        Assert.True(_v.Validate(Valid()).IsValid);
    }

    [Fact]
    public void CitizenAppSource_IsRejected()
    {
        var d = Valid();
        d.Source = IncidentSource.CitizenApp;

        Assert.Contains(_v.Validate(d).Errors, e => e.PropertyName == nameof(CreateOperatorIncidentDto.Source));
    }

    [Fact]
    public void EventTypeNone_IsRejected()
    {
        var d = Valid();
        d.EventType = IncidentEventType.None;

        Assert.False(_v.Validate(d).IsValid);
    }

    [Fact]
    public void UnclassifiedSeverity_IsRejected()
    {
        var d = Valid();
        d.Severity = SeverityLevel.Unclassified;

        Assert.False(_v.Validate(d).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   abc  ")]
    public void OperatorNotesShorterThanFive_IsRejected(string notes)
    {
        var d = Valid();
        d.OperatorNotes = notes;

        Assert.Contains(_v.Validate(d).Errors, e => e.PropertyName == nameof(CreateOperatorIncidentDto.OperatorNotes));
    }
}

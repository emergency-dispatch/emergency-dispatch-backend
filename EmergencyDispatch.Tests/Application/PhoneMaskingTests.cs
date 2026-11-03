using EmergencyDispatch.Application.Services;

namespace EmergencyDispatch.Tests.Application;

public class PhoneMaskingTests
{
    [Theory]
    [InlineData("0901234567", "0901234567")]
    [InlineData("090 123 4567", "0901234567")]
    [InlineData("+84901234567", "0901234567")]
    [InlineData("84901234567", "0901234567")]
    [InlineData("(090)-123.4567", "0901234567")]
    public void Normalize_StripsFormattingAndCountryCode(string raw, string expected)
    {
        Assert.Equal(expected, PhoneMasking.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Normalize_NoDigits_ReturnsNull(string? raw)
    {
        Assert.Null(PhoneMasking.Normalize(raw));
    }

    [Fact]
    public void Normalize_ShortNumberStartingWith84_IsNotRewritten()
    {
        Assert.Equal("8412", PhoneMasking.Normalize("8412"));
    }

    [Theory]
    [InlineData("0901234567", "090***567")]
    [InlineData("+84 90 123 4567", "090***567")]
    [InlineData("1234567", "123***567")]
    public void Mask_KeepsThreeFirstAndThreeLastDigits(string raw, string expected)
    {
        Assert.Equal(expected, PhoneMasking.Mask(raw));
    }

    [Theory]
    [InlineData("123456", "******")]
    [InlineData("12", "**")]
    public void Mask_ShortNumber_IsFullyHidden(string raw, string expected)
    {
        Assert.Equal(expected, PhoneMasking.Mask(raw));
    }

    [Fact]
    public void Mask_Null_ReturnsNull()
    {
        Assert.Null(PhoneMasking.Mask(null));
    }
}

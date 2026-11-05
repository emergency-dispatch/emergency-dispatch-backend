using System.Net;
using EmergencyDispatch.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmergencyDispatch.Tests.Infrastructure;

/// <summary>
/// Render gói free chặn SMTP → có EmailSettings:BrevoApiKey thì gửi qua Brevo HTTPS API
/// </summary>
public class EmailServiceBrevoTests
{
    private static EmailService Service(FakeHttpHandler handler, string? brevoKey = "brevo-key")
    {
        var config = TestHelpers.Config(
            ("EmailSettings:SenderEmail", "sender@example.com"),
            ("EmailSettings:SenderPassword", "app-password"),
            ("EmailSettings:SenderName", "Emergency Dispatch System"),
            ("EmailSettings:BrevoApiKey", brevoKey));
        return new EmailService(config, NullLogger<EmailService>.Instance,
            new FakeHttpClientFactory().With(EmailService.BrevoHttpClientName, handler));
    }

    [Fact]
    public async Task WithBrevoKey_PostsToBrevoApi_WithSenderRecipientAndKey()
    {
        var handler = FakeHttpHandler.Json("{\"messageId\":\"1\"}", HttpStatusCode.Created);

        await Service(handler).SendVerificationEmailAsync("user@example.com", "An", "123456");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", request.RequestUri!.ToString());
        Assert.Equal("brevo-key", request.Headers.GetValues("api-key").Single());
        var body = handler.Bodies.Single();
        Assert.Contains("\"email\":\"sender@example.com\"", body);
        Assert.Contains("\"email\":\"user@example.com\"", body);
        Assert.Contains("123456", body);
    }

    [Fact]
    public async Task BrevoRejects_DoesNotThrow()
    {
        var handler = FakeHttpHandler.Json("{\"code\":\"unauthorized\"}", HttpStatusCode.Unauthorized);

        await Service(handler).SendPasswordChangedAlertAsync("user@example.com", "An", DateTime.UtcNow);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task WithoutBrevoKey_DoesNotCallBrevo()
    {
        var handler = FakeHttpHandler.Json("{}", HttpStatusCode.Created);
        var config = TestHelpers.Config(("EmailSettings:SenderEmail", "sender@example.com"));   // không có mật khẩu → mô phỏng

        await new EmailService(config, NullLogger<EmailService>.Instance,
            new FakeHttpClientFactory().With(EmailService.BrevoHttpClientName, handler)).SendWelcomeEmailAsync("user@example.com", "An");

        Assert.Empty(handler.Requests);
    }
}

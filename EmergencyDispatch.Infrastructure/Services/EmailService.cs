using System.Net;
using System.Net.Mail;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Email tài khoản (tiếng Anh, mẫu tối giản). Style viết inline để Gmail / Outlook không bỏ mất định dạng.
/// </summary>
public class EmailService : IEmailService
{
    private const string Brand = "Emergency Dispatch";
    private const string Footer = "© 2026 Emergency Dispatch System · Hi-Tech Park Rescue Station";

    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendVerificationEmailAsync(string toEmail, string fullName, string token, CancellationToken cancellationToken = default)
    {
        const string accent = "#d32f2f";
        var content =
            Greeting(fullName) +
            Paragraph("Thank you for registering with the Emergency Dispatch System. Enter the verification code below to activate your account.") +
            OtpBox(token, "This code expires in <b>15 minutes</b>. Never share it with anyone, including our staff.") +
            Paragraph("Once activated, you can send a one-tap SOS with photos of the scene and get help from the nearest rescue station.") +
            Muted("If you did not create this account, you can safely ignore this email.");

        await SendEmailAsync(toEmail, "Your Emergency Dispatch verification code",
            Layout(accent, "Verify your account", content), cancellationToken);
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string fullName, CancellationToken cancellationToken = default)
    {
        const string accent = "#1565c0";
        var content =
            Greeting(fullName) +
            Paragraph("Your email has been verified. You can now send SOS reports and follow the rescue team in real time.") +
            $@"<div style=""background:#f5f6f8;border-left:4px solid #111111;padding:14px 18px;border-radius:4px;margin:18px 0"">
                <div style=""font-weight:700;color:#111111;margin-bottom:6px"">Complete your emergency profile</div>
                <ul style=""margin:0 0 8px;padding-left:20px"">
                    <li style=""margin:3px 0""><b>Blood type</b> — helps medics act faster.</li>
                    <li style=""margin:3px 0""><b>Medical notes &amp; allergies</b> — known before treatment.</li>
                    <li style=""margin:3px 0""><b>Emergency contact</b> — so we can reach your family.</li>
                </ul>
                <div style=""font-size:13px;color:#555555"">Open <b>Profile</b> in the app to add them.</div>
            </div>" +
            Paragraph("Stay safe.");

        await SendEmailAsync(toEmail, "Welcome to Emergency Dispatch — your account is active",
            Layout(accent, "Your account is active", content), cancellationToken);
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string fullName, string token, CancellationToken cancellationToken = default)
    {
        const string accent = "#e65100";
        var content =
            Greeting(fullName) +
            Paragraph($"We received a request to reset the password for <b>{Encode(toEmail)}</b>. Use the code below to continue.") +
            OtpBox(token, "This code expires in <b>10 minutes</b>.") +
            Muted("If you did not request a password reset, ignore this email — your password will not change.");

        await SendEmailAsync(toEmail, "Reset your Emergency Dispatch password",
            Layout(accent, "Password reset request", content), cancellationToken);
    }

    public async Task SendPasswordChangedAlertAsync(string toEmail, string fullName, DateTime changedAt, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        const string accent = "#c2185b";
        var ipDisplay = string.IsNullOrWhiteSpace(ipAddress) ? "Unknown" : Encode(ipAddress);
        var timeDisplay = changedAt.ToString("HH:mm:ss dd/MM/yyyy") + " UTC";

        var content =
            Greeting(fullName) +
            Paragraph($"The password for <b>{Encode(toEmail)}</b> was changed successfully.") +
            $@"<table style=""width:100%;border-collapse:collapse;margin:16px 0;font-size:14px"">
                {Row("Time", timeDisplay)}
                {Row("IP address", ipDisplay)}
                {Row("Sessions", "All other sessions have been signed out.")}
            </table>
            <div style=""background:#ffebee;border-left:4px solid #d32f2f;padding:12px 14px;color:#b71c1c;font-size:13px;margin:16px 0"">
                <b>Wasn't you?</b> Your account may be compromised. Use <b>Forgot password</b> right away or contact the administrator.
            </div>";

        await SendEmailAsync(toEmail, "Security alert: your password was changed",
            Layout(accent, "Your password was changed", content), cancellationToken);
    }

    // ============================== Mẫu dùng chung ==============================

    private static string Layout(string accent, string title, string content) => $@"<!DOCTYPE html>
<html>
<head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1""></head>
<body style=""margin:0;padding:24px 0;background:#f6f7f9;font-family:'Segoe UI',Arial,sans-serif;color:#333333;line-height:1.6"">
    <div style=""max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e3e6ea;border-top:4px solid {accent};border-radius:6px"">
        <div style=""padding:22px 28px 0;font-size:14px;font-weight:700;color:{accent};letter-spacing:.5px"">{Brand}</div>
        <div style=""padding:6px 28px 0;font-size:22px;font-weight:600;color:#111111"">{title}</div>
        <div style=""padding:16px 28px 24px;font-size:15px"">{content}</div>
    </div>
    <div style=""max-width:560px;margin:14px auto 0;text-align:center;font-size:12px;color:#8a929b;line-height:1.5"">
        {Footer}<br>This is an automated message. Please do not reply.
    </div>
</body>
</html>";

    private static string Greeting(string fullName) => Paragraph($"Hi <b>{Encode(fullName)}</b>,");

    private static string Paragraph(string html) => $@"<p style=""margin:0 0 14px"">{html}</p>";

    private static string Muted(string html) => $@"<p style=""margin:0;font-size:13px;color:#888888"">{html}</p>";

    private static string OtpBox(string code, string note) => $@"
            <div style=""background:#f5f6f8;border:2px dashed #111111;border-radius:8px;text-align:center;padding:18px;margin:20px 0"">
                <div style=""font-size:34px;font-weight:700;letter-spacing:8px;color:#111111;font-family:Consolas,monospace"">{Encode(code)}</div>
                <div style=""font-size:13px;color:#666666;margin-top:8px"">{note}</div>
            </div>";

    private static string Row(string label, string value) =>
        $@"<tr><td style=""padding:9px 0;border-bottom:1px solid #eeeeee;color:#666666;width:38%"">{label}</td><td style=""padding:9px 0;border-bottom:1px solid #eeeeee"">{value}</td></tr>";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private async Task SendEmailAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var host = _configuration["EmailSettings:Host"] ?? _configuration["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
        var portStr = _configuration["EmailSettings:Port"] ?? _configuration["EmailSettings:SmtpPort"] ?? "587";
        int.TryParse(portStr, out var port);
        if (port <= 0) port = 587;

        var username = _configuration["EmailSettings:Username"] ?? _configuration["EmailSettings:SenderEmail"] ?? string.Empty;
        var password = _configuration["EmailSettings:Password"] ?? _configuration["EmailSettings:SenderPassword"] ?? string.Empty;
        var fromEmail = _configuration["EmailSettings:FromEmail"] ?? _configuration["EmailSettings:SenderEmail"] ?? "no-reply@emergencydispatch.com";
        var fromName = _configuration["EmailSettings:FromName"] ?? _configuration["EmailSettings:SenderName"] ?? "Emergency Dispatch System";
        var enableSsl = bool.TryParse(_configuration["EmailSettings:EnableSsl"], out var ssl) ? ssl : true;

        // Nếu chưa cấu hình mật khẩu SMTP (môi trường dev): Ghi log thông báo và mô phỏng gửi thành công
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || password.Contains("YOUR_"))
        {
            _logger.LogWarning("📧 [Mô phỏng Email] SMTP chưa được cấu hình. Gửi email ảo tới: {ToEmail} | Tiêu đề: {Subject}", toEmail, subject);
            _logger.LogInformation("Nội dung HTML mô phỏng tóm tắt:\n{HtmlBodySnippet}", htmlBody.Length > 250 ? htmlBody[..250] + "..." : htmlBody);
            return;
        }

        try
        {
            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = enableSsl
            };

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Đã gửi email thành công tới {ToEmail} qua SMTP server {Host}:{Port}", toEmail, host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi email qua SMTP tới {ToEmail}. Tiêu đề: {Subject}", toEmail, subject);
            // Không re-throw để tránh crash các luồng nghiệp vụ chính nếu SMTP bên thứ 3 gián đoạn
        }
    }
}

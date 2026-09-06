using System.Net;
using System.Net.Mail;
using EmergencyDispatch.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendVerificationEmailAsync(string toEmail, string fullName, string token, CancellationToken cancellationToken = default)
    {
        var subject = "🚨 [Emergency Dispatch] Mã xác thực đăng ký tài khoản cứu hộ";
        var body = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f6f9; margin: 0; padding: 0; }}
                .container {{ max-width: 600px; margin: 30px auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.08); }}
                .header {{ background: linear-gradient(135deg, #d32f2f, #b71c1c); color: white; padding: 25px 20px; text-align: center; }}
                .header h1 {{ margin: 0; font-size: 24px; letter-spacing: 0.5px; }}
                .content {{ padding: 30px; color: #333333; line-height: 1.6; }}
                .otp-box {{ background: #fff3f3; border: 2px dashed #d32f2f; border-radius: 8px; text-align: center; padding: 18px; margin: 25px 0; }}
                .otp-code {{ font-size: 36px; font-weight: bold; letter-spacing: 6px; color: #d32f2f; margin: 0; }}
                .note {{ font-size: 13px; color: #777777; margin-top: 15px; }}
                .footer {{ background: #f8f9fa; text-align: center; padding: 20px; font-size: 12px; color: #888888; border-top: 1px solid #eeeeee; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1>🚨 EMERGENCY DISPATCH SYSTEM</h1>
                    <p style='margin: 5px 0 0; font-size: 14px; opacity: 0.9;'>Hệ thống Cứu nạn & Điều phối Khẩn cấp</p>
                </div>
                <div class='content'>
                    <p>Xin chào <strong>{fullName}</strong>,</p>
                    <p>Cảm ơn bạn đã đăng ký tài khoản tham gia mạng lưới cứu nạn khẩn cấp. Để hoàn tất kích hoạt tài khoản, vui lòng sử dụng mã xác thực (OTP) dưới đây:</p>
                    <div class='otp-box'>
                        <div class='otp-code'>{token}</div>
                        <p class='note'>Mã xác thực có hiệu lực trong vòng <strong>15 phút</strong>. Tuyệt đối không chia sẻ mã này cho bất kỳ ai.</p>
                    </div>
                    <p>Sau khi kích hoạt, bạn có thể thực hiện gửi tín hiệu SOS 1 chạm, đính kèm ảnh/video hiện trường và nhận sự hỗ trợ kịp thời từ các trạm cứu hộ gần nhất.</p>
                </div>
                <div class='footer'>
                    <p>© 2026 Emergency Dispatch System. Trung tâm Điều phối Cứu nạn Cứu hộ Quốc gia.</p>
                    <p>Hotline khẩn cấp: 114 (Cứu hỏa) | 115 (Cấp cứu y tế) | 112 (Cứu nạn)</p>
                </div>
            </div>
        </body>
        </html>";

        await SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string fullName, CancellationToken cancellationToken = default)
    {
        var subject = "🎉 Chào mừng bạn gia nhập Hệ thống Cứu nạn Cứu hộ Khẩn cấp";
        var body = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f6f9; margin: 0; padding: 0; }}
                .container {{ max-width: 600px; margin: 30px auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.08); }}
                .header {{ background: linear-gradient(135deg, #1565c0, #0d47a1); color: white; padding: 25px 20px; text-align: center; }}
                .header h1 {{ margin: 0; font-size: 22px; }}
                .content {{ padding: 30px; color: #333333; line-height: 1.6; }}
                .highlight-card {{ background: #e3f2fd; border-left: 4px solid #1565c0; padding: 15px 20px; border-radius: 4px; margin: 20px 0; }}
                .footer {{ background: #f8f9fa; text-align: center; padding: 20px; font-size: 12px; color: #888888; border-top: 1px solid #eeeeee; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1>🛡️ TÀI KHOẢN ĐÃ KÍCH HOẠT THÀNH CÔNG</h1>
                </div>
                <div class='content'>
                    <p>Xin chào <strong>{fullName}</strong>,</p>
                    <p>Tài khoản của bạn đã được xác thực thành công. Bạn hiện đã kết nối trực tiếp với trung tâm chỉ huy cứu nạn khẩn cấp.</p>
                    
                    <div class='highlight-card'>
                        <h4 style='margin-top:0; color:#0d47a1;'>⚠️ LƯU Ý ĐẶC THÙ CỨU HỘ:</h4>
                        <p style='margin-bottom:0;'>Vì tính chất khẩn cấp sống còn, xin vui lòng truy cập mục <strong>Hồ sơ cá nhân</strong> để cập nhật đầy đủ:
                        <ul style='padding-left: 20px; margin-top: 8px;'>
                            <li><strong>Nhóm máu (Blood Type)</strong>: Phục vụ truyền máu tức thì khi có tai nạn.</li>
                            <li><strong>Tiền sử bệnh án & Dị ứng thuốc</strong>: Bác sĩ cấp cứu cần nắm trước.</li>
                            <li><strong>Số điện thoại người thân khẩn cấp</strong>: Để thông báo ngay cho gia đình.</li>
                        </ul>
                        </p>
                    </div>
                    <p>Chúc bạn và gia đình luôn an toàn!</p>
                </div>
                <div class='footer'>
                    <p>© 2026 Emergency Dispatch System. Luôn túc trực 24/7 vì sự an toàn của người dân.</p>
                </div>
            </div>
        </body>
        </html>";

        await SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string fullName, string token, CancellationToken cancellationToken = default)
    {
        var subject = "🔒 [Emergency Dispatch] Yêu cầu đặt lại mật khẩu tài khoản";
        var body = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f6f9; margin: 0; padding: 0; }}
                .container {{ max-width: 600px; margin: 30px auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.08); }}
                .header {{ background: linear-gradient(135deg, #e65100, #bf360c); color: white; padding: 25px 20px; text-align: center; }}
                .content {{ padding: 30px; color: #333333; line-height: 1.6; }}
                .otp-box {{ background: #fff8e1; border: 2px dashed #ff8f00; border-radius: 8px; text-align: center; padding: 18px; margin: 25px 0; }}
                .otp-code {{ font-size: 36px; font-weight: bold; letter-spacing: 6px; color: #e65100; margin: 0; }}
                .footer {{ background: #f8f9fa; text-align: center; padding: 20px; font-size: 12px; color: #888888; border-top: 1px solid #eeeeee; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1 style='margin:0; font-size:22px;'>🔒 ĐẶT LẠI MẬT KHẨU</h1>
                </div>
                <div class='content'>
                    <p>Xin chào <strong>{fullName}</strong>,</p>
                    <p>Hệ thống nhận được yêu cầu đặt lại mật khẩu cho tài khoản <strong>{toEmail}</strong>. Sử dụng mã OTP dưới đây để hoàn tất:</p>
                    <div class='otp-box'>
                        <div class='otp-code'>{token}</div>
                        <p style='font-size:13px; color:#666; margin-top:10px;'>Mã có hiệu lực trong vòng <strong>10 phút</strong>.</p>
                    </div>
                    <p style='color: #c62828; font-size: 13px;'>* Nếu bạn không yêu cầu đặt lại mật khẩu, xin vui lòng bỏ qua email này hoặc liên hệ hỗ trợ ngay lập tức.</p>
                </div>
                <div class='footer'>
                    <p>© 2026 Emergency Dispatch System. Trung tâm Điều phối Cứu nạn Cứu hộ Quốc gia.</p>
                </div>
            </div>
        </body>
        </html>";

        await SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

    public async Task SendPasswordChangedAlertAsync(string toEmail, string fullName, DateTime changedAt, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var subject = "⚠️ [Cảnh báo Bảo mật] Mật khẩu tài khoản của bạn vừa được thay đổi";
        var ipDisplay = string.IsNullOrWhiteSpace(ipAddress) ? "Không xác định" : ipAddress;
        var timeDisplay = changedAt.ToString("HH:mm:ss dd/MM/yyyy UTC");

        var body = $@"
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset='utf-8'>
            <style>
                body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f4f6f9; margin: 0; padding: 0; }}
                .container {{ max-width: 600px; margin: 30px auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 15px rgba(0,0,0,0.08); }}
                .header {{ background: linear-gradient(135deg, #c2185b, #880e4f); color: white; padding: 25px 20px; text-align: center; }}
                .content {{ padding: 30px; color: #333333; line-height: 1.6; }}
                .info-table {{ width: 100%; border-collapse: collapse; margin: 20px 0; }}
                .info-table td {{ padding: 10px; border-bottom: 1px solid #eeeeee; font-size: 14px; }}
                .warning-box {{ background: #ffebee; border-left: 4px solid #d32f2f; padding: 15px; margin: 20px 0; color: #b71c1c; font-size: 13px; }}
                .footer {{ background: #f8f9fa; text-align: center; padding: 20px; font-size: 12px; color: #888888; border-top: 1px solid #eeeeee; }}
            </style>
        </head>
        <body>
            <div class='container'>
                <div class='header'>
                    <h1 style='margin:0; font-size:22px;'>⚠️ CẢNH BÁO BẢO MẬT TÀI KHOẢN</h1>
                </div>
                <div class='content'>
                    <p>Xin chào <strong>{fullName}</strong>,</p>
                    <p>Mật khẩu của tài khoản <strong>{toEmail}</strong> vừa được thay đổi thành công với thông tin chi tiết:</p>
                    
                    <table class='info-table'>
                        <tr>
                            <td><strong>Thời gian thay đổi:</strong></td>
                            <td>{timeDisplay}</td>
                        </tr>
                        <tr>
                            <td><strong>Địa chỉ IP thực hiện:</strong></td>
                            <td>{ipDisplay}</td>
                        </tr>
                        <tr>
                            <td><strong>Trạng thái bảo mật:</strong></td>
                            <td>Toàn bộ các phiên đăng nhập cũ đã được thu hồi an toàn.</td>
                        </tr>
                    </table>

                    <div class='warning-box'>
                        <strong>CHÚ Ý:</strong> Nếu bạn KHÔNG thực hiện thay đổi này, tài khoản của bạn có thể đã bị xâm phạm. Vui lòng bấm <strong>Quên mật khẩu</strong> để khôi phục ngay lập tức hoặc liên hệ quản trị viên.
                    </div>
                </div>
                <div class='footer'>
                    <p>© 2026 Emergency Dispatch System. Trung tâm Điều phối Cứu nạn Cứu hộ Quốc gia.</p>
                </div>
            </div>
        </body>
        </html>";

        await SendEmailAsync(toEmail, subject, body, cancellationToken);
    }

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

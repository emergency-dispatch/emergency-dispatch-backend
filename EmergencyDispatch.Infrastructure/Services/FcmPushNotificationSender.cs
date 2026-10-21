using EmergencyDispatch.Application.DTOs.Notification;
using EmergencyDispatch.Application.Interfaces;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmergencyDispatch.Infrastructure.Services;

/// <summary>
/// Gửi push qua Firebase Cloud Messaging (HTTP v1). Cấu hình "Firebase:CredentialPath" trỏ tới file service-account JSON;
/// nếu chưa cấu hình thì chỉ ghi log (không gửi) để môi trường dev vẫn chạy được.
/// </summary>
public class FcmPushNotificationSender : IPushNotificationSender
{
    public const string EmergencyChannelId = "EMERGENCY_DISPATCH";

    private static readonly object InitLock = new();
    private static FirebaseApp? _app;
    private static bool _initAttempted;

    private readonly ILogger<FcmPushNotificationSender> _logger;

    public FcmPushNotificationSender(IConfiguration configuration, ILogger<FcmPushNotificationSender> logger)
    {
        _logger = logger;
        EnsureInitialized(configuration, logger);
    }

    public async Task<PushSendResult> SendAsync(
        string deviceToken,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        bool highPriority,
        CancellationToken cancellationToken = default)
    {
        if (_app == null)
        {
            _logger.LogInformation("[FCM chưa cấu hình] Bỏ qua push tới {Token}: {Title}", Shorten(deviceToken), title);
            return new PushSendResult { Success = false, Error = "FCM_NOT_CONFIGURED" };
        }

        var message = new Message
        {
            Token = deviceToken,
            Notification = new Notification { Title = title, Body = body },
            Data = data.ToDictionary(kv => kv.Key, kv => kv.Value),
            Android = new AndroidConfig
            {
                Priority = highPriority ? Priority.High : Priority.Normal,
                Notification = new AndroidNotification
                {
                    ChannelId = highPriority ? EmergencyChannelId : null,
                    Sound = highPriority ? "emergency_alarm" : "default"
                }
            },
            Apns = new ApnsConfig
            {
                Headers = new Dictionary<string, string> { ["apns-priority"] = highPriority ? "10" : "5" },
                Aps = new Aps { Sound = highPriority ? "emergency_alarm.caf" : "default" }
            }
        };

        try
        {
            var id = await FirebaseMessaging.GetMessaging(_app).SendAsync(message, cancellationToken);
            return new PushSendResult { Success = true, MessageId = id };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Gửi FCM thất bại tới {Token}", Shorten(deviceToken));
            return new PushSendResult { Success = false, Error = ex.Message };
        }
    }

    private static void EnsureInitialized(IConfiguration configuration, ILogger logger)
    {
        if (_initAttempted) return;
        lock (InitLock)
        {
            if (_initAttempted) return;
            _initAttempted = true;

            var path = configuration["Firebase:CredentialPath"];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                logger.LogWarning("Firebase:CredentialPath chưa cấu hình hoặc không tồn tại — push FCM chỉ được ghi log.");
                return;
            }

            try
            {
                _app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(new AppOptions
                {
#pragma warning disable CS0618 // FromFile vẫn là cách đơn giản nhất để nạp service-account JSON
                    Credential = GoogleCredential.FromFile(path)
#pragma warning restore CS0618
                });
                logger.LogInformation("Đã khởi tạo Firebase Admin SDK cho push notification.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Không khởi tạo được Firebase Admin SDK — push FCM chỉ được ghi log.");
            }
        }
    }

    private static string Shorten(string token) => token.Length <= 12 ? token : $"{token[..6]}…{token[^6..]}";
}

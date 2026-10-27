using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using EmergencyDispatch.API.Filters;
using EmergencyDispatch.API.Hubs;
using EmergencyDispatch.API.Realtime;
using EmergencyDispatch.Application.Interfaces;
using EmergencyDispatch.Application.Localization;
using EmergencyDispatch.Application.Services;
using EmergencyDispatch.Domain.Interfaces;
using EmergencyDispatch.Infrastructure.BackgroundJobs;
using FluentValidation;
using EmergencyDispatch.Infrastructure.Data;
using EmergencyDispatch.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Controllers + JSON Enum serialize as string + SignalR
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .ConfigureApiBehaviorOptions(LocalizedValidationResponseFactory.Configure);

builder.Services.AddSignalR(options =>
    {
        // Chặn lời gọi hub từ kết nối của tài khoản vừa bị khóa / đổi quyền
        options.AddFilter<EmergencyDispatch.API.Services.UserSessionHubFilter>();
    })
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// 2. CORS (Cho phép Frontend gọi API) — danh sách origin đọc từ "Cors:AllowedOrigins", xem CorsOriginPolicy
var corsOriginPolicy = new EmergencyDispatch.API.Services.CorsOriginPolicy(builder.Configuration, builder.Environment);
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.SetIsOriginAllowed(corsOriginPolicy.IsAllowed)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              // FE đọc được lý do token bị từ chối và tên file báo cáo tải về
              .WithExposedHeaders("X-Auth-Error", "Content-Disposition"));
});

// 2.1. Cấu hình đa ngôn ngữ (Bilingual vi/en Localization)
var supportedCultures = new[] { "vi", "en", "vi-VN", "en-US" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("vi")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

// Bắt ngôn ngữ theo thứ tự: Header Accept-Language -> Query ?culture= -> Cookie
localizationOptions.RequestCultureProviders = new List<IRequestCultureProvider>
{
    new AcceptLanguageHeaderRequestCultureProvider(),
    new QueryStringRequestCultureProvider(),
    new CookieRequestCultureProvider()
};
builder.Services.AddLocalization();
builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

// 3. Database (PostgreSQL)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (!string.IsNullOrEmpty(connectionString))
    {
        options.UseNpgsql(connectionString);
        options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
    }
});

// 4. Swagger / OpenAPI với JWT Bearer hỗ trợ & XML Documentation
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Emergency Dispatch API",
        Version = "v1",
        Description = "API hệ thống điều phối và quản lý đội cứu hộ khẩn cấp theo thời gian thực (ASP.NET Core 9.0 Clean Architecture)"
    });

    var securityScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Nhập JWT Token theo định dạng: Bearer {token}",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securityScheme, Array.Empty<string>() }
    });

    // Tự động load file XML comments để hiển thị mô tả API và DTO
    var xmlFilename = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    var appXmlPath = Path.Combine(AppContext.BaseDirectory, "EmergencyDispatch.Application.xml");
    if (File.Exists(appXmlPath))
    {
        c.IncludeXmlComments(appXmlPath);
    }

    // Hỗ trợ chọn nhanh ngôn ngữ (vi / en) trực tiếp trên giao diện Swagger UI
    c.OperationFilter<AcceptLanguageHeaderOperationFilter>();
});

// 5. Đăng ký Repositories (Scoped)
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<IStationRepository, StationRepository>();
builder.Services.AddScoped<IRescueUnitRepository, RescueUnitRepository>();
builder.Services.AddScoped<IDispatchAssignmentRepository, DispatchAssignmentRepository>();
builder.Services.AddScoped<IIncidentRepository, IncidentRepository>();
builder.Services.AddScoped<ITrafficHotspotRepository, TrafficHotspotRepository>();
builder.Services.AddScoped<IEscalationRepository, EscalationRepository>();
builder.Services.AddScoped<IBlockedPhoneRepository, BlockedPhoneRepository>();
builder.Services.AddScoped<ISystemSettingRepository, SystemSettingRepository>();
builder.Services.AddScoped<IStationTeamRepository, StationTeamRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<ISystemAuditLogRepository, SystemAuditLogRepository>();

// 6. Đăng ký FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<EmergencyDispatch.Application.Validators.CreateIncidentDtoValidator>();

// 7. Cấu hình Resilient HTTP Client cho AI Qwen2.5-VL (OpenRouter) theo FR-SYS-01:
//    timeout 20s/lần, retry 3 lần backoff 1s → 2s → 4s, circuit breaker mở khi 5 lỗi liên tiếp trong 2 phút, nghỉ 30s
builder.Services.AddHttpClient("OpenRouterClient")
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.Retry.UseJitter = false;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.FailureRatio = 1.0;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    });

// 8. Đăng ký Application Services (Scoped)
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IEmailService, EmergencyDispatch.Infrastructure.Services.EmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IStationService, StationService>();
builder.Services.AddScoped<IRescueUnitService, RescueUnitService>();
builder.Services.AddScoped<IAiClassificationService, EmergencyDispatch.Infrastructure.Services.AiClassificationService>();
builder.Services.AddScoped<IMediaUploadService, EmergencyDispatch.Infrastructure.Services.CloudinaryMediaService>();
builder.Services.AddScoped<IIncidentService, IncidentService>();
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IDispatchService, DispatchService>();
builder.Services.AddScoped<ITrafficService, TrafficService>();
builder.Services.AddScoped<IEscalationService, EscalationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IBlockedNumberService, BlockedNumberService>();
builder.Services.AddScoped<IStationTeamService, StationTeamService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddScoped<ISystemAuditService, SystemAuditService>();

// Chỉ đường theo đường bộ (OSRM). Tự host bằng Docker: đổi Routing:OsrmBaseUrl, không cần sửa code
builder.Services.AddHttpClient(EmergencyDispatch.Infrastructure.Services.OsrmRoutingService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Routing:TimeoutSeconds", 5));
    // Máy chủ OSRM demo từ chối (403) request không có User-Agent
    client.DefaultRequestHeaders.UserAgent.ParseAdd("EmergencyDispatch-SEP490/1.0");
});
builder.Services.AddSingleton<IRoutingService, EmergencyDispatch.Infrastructure.Services.OsrmRoutingService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IReportFileGenerator, EmergencyDispatch.Infrastructure.Services.ReportFileGenerator>();

// Người dùng hiện tại + kiểm tra phiên đăng nhập còn hiệu lực (khóa tài khoản có hiệu lực ngay)
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ICurrentUserContext, EmergencyDispatch.API.Services.HttpCurrentUserContext>();
builder.Services.AddSingleton<EmergencyDispatch.API.Services.UserSessionValidator>();
builder.Services.AddSingleton<IUserSessionInvalidator>(sp => sp.GetRequiredService<EmergencyDispatch.API.Services.UserSessionValidator>());
builder.Services.AddSingleton<EmergencyDispatch.API.Services.UserSessionHubFilter>();
builder.Services.AddSingleton<IPushNotificationSender, EmergencyDispatch.Infrastructure.Services.FcmPushNotificationSender>();

// 8.1. Hàng đợi AI nền (Channel Bounded) & Background Worker
builder.Services.AddSingleton<IAiAnalysisQueue, AiAnalysisQueue>();
builder.Services.AddHostedService<AiAnalysisWorker>();
builder.Services.AddHostedService<HotspotCleanupWorker>();
builder.Services.AddHostedService<EscalationWorker>();
builder.Services.AddHostedService<VehicleCoolDownWorker>();
builder.Services.AddHostedService<IncidentAutoExpireWorker>();

// 8.2. Kênh thông báo Realtime (SignalR Notifier)
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

// 9. Cấu hình JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
// Không có key mặc định: key viết cứng trong repo ai cũng đọc được và tự ký token được
var secretKey = jwtSettings["Key"];
if (string.IsNullOrWhiteSpace(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < 32)
{
    throw new InvalidOperationException("JwtSettings:Key chưa được cấu hình hoặc ngắn hơn 32 byte (HMAC-SHA256 cần tối thiểu 256 bit).");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"] ?? "EmergencyDispatchAPI",
        ValidAudience = jwtSettings["Audience"] ?? "EmergencyDispatchClient",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };

    // Hỗ trợ truyền JWT qua query string "access_token" cho kết nối WebSockets / SignalR
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },

        // Token hết hiệu lực ngay khi tài khoản bị khóa / xóa / đổi role / đổi trạm
        OnTokenValidated = async context =>
        {
            var validator = context.HttpContext.RequestServices.GetRequiredService<EmergencyDispatch.API.Services.UserSessionValidator>();
            var reason = await validator.ValidateAsync(context.Principal!, context.HttpContext.RequestAborted);
            if (reason != null)
            {
                context.Fail(reason);
            }
        },

        // Trả lý do cho client (header) để FE hiện thông báo phù hợp thay vì "sai mật khẩu"
        OnChallenge = context =>
        {
            if (context.AuthenticateFailure != null && !string.IsNullOrEmpty(context.AuthenticateFailure.Message))
            {
                context.Response.Headers["X-Auth-Error"] = Uri.EscapeDataString(LocalizationHelper.Localize(context.AuthenticateFailure.Message));
            }
            return Task.CompletedTask;
        }
    };
});

// 10. Authorization
builder.Services.AddAuthorization();
builder.Services.AddDataProtection();
builder.Services.AddScoped<IGuestCancellationTokenService, EmergencyDispatch.API.Services.GuestCancellationTokenService>();

// 11. Rate Limiting chống spam endpoint công khai (BR-CIT-01, chống dò mật khẩu)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // SOS: tối đa 5 yêu cầu / phút / địa chỉ IP
    options.AddPolicy("sos", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    // Đăng nhập: tối đa 10 lần / phút / địa chỉ IP
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

// Tự động kiểm tra và seed dữ liệu khi khởi động
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
        }
        await DbInitializer.SeedAsync(context);
        logger.LogInformation("Database migration và seed dữ liệu hoàn tất thành công.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Không thể kết nối hoặc tự động migrate database (vui lòng kiểm tra connection string).");
    }
}

// Pipeline Middleware
if (!corsOriginPolicy.HasExplicitOrigins)
{
    if (corsOriginPolicy.AllowsLocalNetwork)
    {
        app.Logger.LogWarning("Chưa cấu hình Cors:AllowedOrigins — đang cho phép localhost và IP mạng LAN (chỉ dùng khi phát triển).");
    }
    else
    {
        app.Logger.LogWarning("Chưa cấu hình Cors:AllowedOrigins — mọi request từ trình duyệt khác origin sẽ bị chặn.");
    }
}

app.UseCors("Frontend");

// Đặt RequestLocalization trước Authentication & Authorization để mọi middleware đều nhận đúng ngôn ngữ
app.UseRequestLocalization(localizationOptions);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Emergency Dispatch API v1");
        c.RoutePrefix = "swagger";
        c.EnablePersistAuthorization();
        c.DisplayRequestDuration();
        c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    });
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// SignalR Hub endpoints
app.MapHub<IncidentHub>("/hubs/incidents");
app.MapHub<TrackingHub>("/hubs/tracking");

app.Run();

using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.SignalR;

namespace EmergencyDispatch.API.Hubs;

/// <summary>
/// Lời gọi hub chạy ngoài pipeline HTTP → áp lại ngôn ngữ của request lúc kết nối
/// (Accept-Language / ?culture=) để HubException và thông báo trả về đúng ngôn ngữ người dùng.
/// </summary>
public class HubCultureFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var requestCulture = invocationContext.Context.GetHttpContext()?.Features.Get<IRequestCultureFeature>()?.RequestCulture;
        if (requestCulture == null)
        {
            return await next(invocationContext);
        }

        var (culture, uiCulture) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = requestCulture.Culture;
        CultureInfo.CurrentUICulture = requestCulture.UICulture;
        try
        {
            return await next(invocationContext);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}

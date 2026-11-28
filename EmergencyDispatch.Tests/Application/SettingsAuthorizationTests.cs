using System.Reflection;
using System.Security.Claims;
using EmergencyDispatch.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace EmergencyDispatch.Tests.Application;

public class SettingsAuthorizationTests
{
    public static IEnumerable<object[]> AccessCases()
    {
        var operatorReads = new[]
        {
            nameof(SettingsController.GetEscalation),
            nameof(SettingsController.GetDispatch),
            nameof(SettingsController.GetDuplicateDetection),
            nameof(SettingsController.GetDefaults)
        };
        var adminWrites = new[]
        {
            nameof(SettingsController.UpdateEscalation),
            nameof(SettingsController.UpdateDispatch),
            nameof(SettingsController.UpdateServiceArea),
            nameof(SettingsController.UpdateDuplicateDetection),
            nameof(SettingsController.UpdateCitizenIncidentAlerts)
        };
        foreach (var role in new[] { "", "Citizen", "RescueStaff", "Operator", "Admin" })
        {
            yield return new object[] { nameof(SettingsController.GetCitizenIncidentAlerts), role, role.Length > 0 };
            foreach (var action in operatorReads)
                yield return new object[] { action, role, role is "Operator" or "Admin" };
            foreach (var action in adminWrites)
                yield return new object[] { action, role, role == "Admin" };
        }
    }

    [Theory]
    [MemberData(nameof(AccessCases))]
    public async Task CombinedControllerAndActionPolicy_EnforcesAccess(string action, string role, bool allowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var controller = typeof(SettingsController);
        var method = controller.GetMethod(action)!;
        Assert.Empty(controller.GetCustomAttributes().OfType<IAllowAnonymous>());
        Assert.Empty(method.GetCustomAttributes().OfType<IAllowAnonymous>());
        // Evaluate the combined controller/action policy, not just individual
        // attributes: action authorization does not override controller roles.
        var metadata = controller.GetCustomAttributes().OfType<IAuthorizeData>()
            .Concat(method.GetCustomAttributes().OfType<IAuthorizeData>());
        var policy = await AuthorizationPolicy.CombineAsync(provider.GetRequiredService<IAuthorizationPolicyProvider>(), metadata);
        Assert.NotNull(policy);
        var identity = role.Length == 0 ? new ClaimsIdentity() : new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "7f2ae665-f109-4f03-9eb1-c797d39e4324"),
            new Claim(ClaimTypes.Role, role)
        }, "Test");
        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(new ClaimsPrincipal(identity), null, policy);
        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public void PublicServiceArea_RemainsTheOnlyAnonymousSettingsAction()
    {
        var actions = typeof(SettingsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes().OfType<IAllowAnonymous>().Any())
            .Select(method => method.Name).ToArray();
        Assert.Equal(new[] { nameof(SettingsController.GetServiceArea) }, actions);
    }
}

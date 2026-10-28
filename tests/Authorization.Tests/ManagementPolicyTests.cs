using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Resort.Application.Common.Utility;
using Resort.Web.Controllers;
using Xunit;

namespace Authorization.Tests;

public class ManagementPolicyTests
{
    [Theory]
    [InlineData(typeof(VillaController), "anonymous", false)]
    [InlineData(typeof(VillaController), "customer", false)]
    [InlineData(typeof(VillaController), "admin", true)]
    [InlineData(typeof(DashboardController), "anonymous", false)]
    [InlineData(typeof(DashboardController), "customer", false)]
    [InlineData(typeof(DashboardController), "admin", true)]
    public async Task EveryManagementActionEnforcesAdministratorPolicy(Type controller, string caller, bool allowed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, caller),
            new Claim(ClaimTypes.Role, caller == "admin" ? SD.Role_Admin : SD.Role_Customer)
        }, caller == "anonymous" ? null : "Test"));

        var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var attributes = controller.GetCustomAttributes(true).Concat(action.GetCustomAttributes(true)).ToArray();
            Assert.DoesNotContain(attributes, a => a is IAllowAnonymous);
            var policy = await AuthorizationPolicy.CombineAsync(policyProvider, attributes.OfType<IAuthorizeData>());
            Assert.NotNull(policy);
            var result = await authorization.AuthorizeAsync(principal, resource: null, policy);
            Assert.Equal(allowed, result.Succeeded);
        }
    }
}

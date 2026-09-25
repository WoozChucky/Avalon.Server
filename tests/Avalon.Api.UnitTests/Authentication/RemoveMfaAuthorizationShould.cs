using System.Reflection;
using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Config;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// Removing MFA is the one way back into an account without its authenticator, so anyone short
/// of Admin must be refused. The policy is read off the real controller and action attributes and
/// evaluated with the API's real authorization setup, down to the status code a caller sees.
/// </summary>
public class RemoveMfaAuthorizationShould
{
    private const long CallerId = 7;

    [Theory]
    [InlineData(AvalonRoles.Player)]
    [InlineData(AvalonRoles.GameMaster)]
    public async Task Forbid_a_caller_below_admin(string role)
    {
        (int status, bool reachedAction) = await CallAsync(role);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(reachedAction);
    }

    [Theory]
    [InlineData(AvalonRoles.Admin)]
    [InlineData(AvalonRoles.Console)]
    public async Task Let_an_admin_through(string role)
    {
        (_, bool reachedAction) = await CallAsync(role);

        Assert.True(reachedAction);
    }

    private static async Task<(int Status, bool ReachedAction)> CallAsync(string role)
    {
        IAccountService accounts = Substitute.For<IAccountService>();
        accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(new Account
            {
                Id = new AccountId(CallerId), Username = "CALLER", Email = "c@avalon.monster",
                Salt = [1], Verifier = [2], JoinDate = DateTime.UtcNow,
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(accounts);
        services.AddSingleton(Substitute.For<IPersonalAccessTokenService>());
        services.AddAuth(new ApplicationConfig
        {
            Authentication = new AuthenticationConfig
            {
                IssuerSigningKey = new string('k', 64),
                Issuer = "avalon",
                Audience = "avalon",
            }
        });

        await using ServiceProvider root = services.BuildServiceProvider();
        await using AsyncServiceScope scope = root.CreateAsyncScope();
        IServiceProvider sp = scope.ServiceProvider;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, CallerId.ToString()),
            new(ClaimTypes.GroupSid, role),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme));

        var context = new DefaultHttpContext { RequestServices = sp, User = user };
        context.Request.Headers[HeaderNames.Authorization] = "Bearer token";
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = context;

        MethodInfo action = typeof(AccountController).GetMethod(nameof(AccountController.RemoveMfa))!;
        IEnumerable<IAuthorizeData> authorizeData = typeof(AccountController)
            .GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true));

        AuthorizationPolicy policy = (await AuthorizationPolicy.CombineAsync(
            sp.GetRequiredService<IAuthorizationPolicyProvider>(), authorizeData))!;

        PolicyAuthorizationResult result = await sp.GetRequiredService<IPolicyEvaluator>().AuthorizeAsync(policy,
            AuthenticateResult.Success(new AuthenticationTicket(user, JwtBearerDefaults.AuthenticationScheme)),
            context, resource: null);

        var reachedAction = false;
        await sp.GetRequiredService<IAuthorizationMiddlewareResultHandler>().HandleAsync(_ =>
        {
            reachedAction = true;
            return Task.CompletedTask;
        }, context, policy, result);

        return (context.Response.StatusCode, reachedAction);
    }
}

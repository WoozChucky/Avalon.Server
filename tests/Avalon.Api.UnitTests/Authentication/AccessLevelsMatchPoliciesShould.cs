using System.Security.Claims;
using Avalon.Api;
using Avalon.Api.Authentication;
using Avalon.Api.Config;
using Avalon.Common.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// The world server gates GM commands with AccessLevels; the API gates GM endpoints with its own
/// policies. They guard the same privilege on two surfaces, so they must not drift.
/// </summary>
public class AccessLevelsMatchPoliciesShould
{
    [Theory]
    [InlineData(AvalonRoles.Console, AccessLevels.Console)]
    [InlineData(AvalonRoles.Admin, AccessLevels.Admin)]
    [InlineData(AvalonRoles.GameMaster, AccessLevels.GameMaster)]
    [InlineData(AvalonRoles.Player, AccessLevels.Player)]
    public void Allow_Exactly_The_Levels_The_Api_Policy_Allows(string policyName, AccountAccessLevel mask)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuth(new ApplicationConfig { Authentication = ApiAuthHost.AuthConfig });

        AuthorizationOptions options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        AuthorizationPolicy policy = options.GetPolicy(policyName)
            ?? throw new InvalidOperationException($"No policy named {policyName}");

        AccountAccessLevel allowed = policy.Requirements
            .OfType<ClaimsAuthorizationRequirement>()
            .Where(r => r.ClaimType == ClaimTypes.GroupSid)
            .SelectMany(r => r.AllowedValues ?? [])
            .Select(Enum.Parse<AccountAccessLevel>)
            .Aggregate((AccountAccessLevel)0, (acc, level) => acc | level);

        Assert.Equal(mask, allowed);
    }
}

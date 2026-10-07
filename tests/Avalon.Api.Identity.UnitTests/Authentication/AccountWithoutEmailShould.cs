using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Avalon.Api.Hosting.Authentication.AV;
using Avalon.Api.Testing;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Authentication;

/// <summary>
/// A Steam-only account has no email until it adds recovery credentials (#793): its access token is minted, and its
/// personal access token admits it, with no email claim, where a claim built from the missing email used to throw.
/// </summary>
public sealed class AccountWithoutEmailShould
{
    [Fact]
    public async Task Get_an_access_token_and_be_admitted_by_a_personal_access_token()
    {
        Account account = ApiTestHost.MakeAccount();
        account.Email = null;

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(ApiTestHost.Mint(account));
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Name && c.Value == account.Username);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email);

        string token = "avp_" + new string('A', 43);
        IPersonalAccessTokenRepository pats = Substitute.For<IPersonalAccessTokenRepository>();
        pats.FindByHashAsync(ApiTestHost.TokenHash(token), Arg.Any<CancellationToken>()).Returns(new PersonalAccessToken
        {
            Id = new PersonalAccessTokenId(5),
            AccountId = account.Id,
            Name = "ci",
            TokenPrefix = token[..8],
            Roles = AccountAccessLevel.Player,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        });
        IAccountRepository accounts = Substitute.For<IAccountRepository>();
        accounts.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == account.Id.Value), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(account);
        IOptionsMonitor<AvalonAuthenticationSchemeOptions> options = Substitute.For<IOptionsMonitor<AvalonAuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AvalonAuthenticationSchemeOptions());

        var handler = new AvalonAuthenticationHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default, pats, accounts,
            TimeProvider.System);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Avalon " + token;
        await handler.InitializeAsync(new AuthenticationScheme("AV", "AV", typeof(AvalonAuthenticationHandler)), context);
        AuthenticateResult result = await handler.AuthenticateAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(account.Username, result.Principal.FindFirstValue(ClaimTypes.Name));
        Assert.DoesNotContain(result.Principal.Claims, c => c.Type == ClaimTypes.Email);
    }
}

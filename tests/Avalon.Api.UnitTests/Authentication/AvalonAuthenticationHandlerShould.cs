using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Avalon.Api.Authentication.AV;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

public class AvalonAuthenticationHandlerShould
{
    private readonly IPersonalAccessTokenService _pats = Substitute.For<IPersonalAccessTokenService>();
    private readonly IAccountService _accounts = Substitute.For<IAccountService>();

    private static readonly AvalonAuthenticationSchemeOptions Options = new();

    private async Task<AuthenticateResult> Authenticate(string? header)
    {
        var monitor = Substitute.For<IOptionsMonitor<AvalonAuthenticationSchemeOptions>>();
        monitor.Get(Arg.Any<string>()).Returns(Options);

        var handler = new AvalonAuthenticationHandler(monitor, NullLoggerFactory.Instance, UrlEncoder.Default, _pats, _accounts);
        var context = new DefaultHttpContext();
        if (header is not null) context.Request.Headers["Authorization"] = header;

        var scheme = new AuthenticationScheme("AV", "AV", typeof(AvalonAuthenticationHandler));
        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    private static Account MakeAccount(AccountAccessLevel roles = AccountAccessLevel.Player, AccountStatus status = AccountStatus.Active) => new()
    {
        Id = new AccountId(7), Username = "u", Email = "u@t",
        Salt = new byte[] {1}, Verifier = new byte[] {2},
        JoinDate = DateTime.UtcNow, AccessLevel = roles, Status = status,
    };

    private static PersonalAccessToken MakePat(string token, AccountAccessLevel roles = AccountAccessLevel.Player,
        DateTime? expiresAt = null, DateTime? revokedAt = null) => new()
    {
        Id = new PersonalAccessTokenId(5),
        AccountId = new AccountId(7),
        TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token)),
        Name = "ci",
        TokenPrefix = token[..8],
        Roles = roles,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(1),
        RevokedAt = revokedAt,
    };

    [Fact] public async Task NoResult_WhenHeaderMissing() =>
        Assert.False((await Authenticate(null)).Succeeded);

    [Fact]
    public async Task NoResult_WhenSchemePrefixIsBearer()
    {
        var result = await Authenticate("Bearer xyz");
        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task Fail_WhenTokenPrefixWrong()
    {
        var result = await Authenticate("Avalon not-a-pat");
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Fail_WhenTokenLengthWrong()
    {
        var result = await Authenticate("Avalon avp_short");
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Fail_WhenTokenUnknown()
    {
        var valid = "avp_" + new string('A', 43);
        _pats.FindByRawTokenAsync(valid, Arg.Any<CancellationToken>()).Returns((PersonalAccessToken?)null);

        var result = await Authenticate("Avalon " + valid);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Fail_WhenTokenRevoked()
    {
        var token = "avp_" + new string('A', 43);
        _pats.FindByRawTokenAsync(token, Arg.Any<CancellationToken>())
             .Returns(MakePat(token, revokedAt: DateTime.UtcNow.AddMinutes(-1)));
        _accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(MakeAccount());

        var result = await Authenticate("Avalon " + token);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Fail_WhenTokenExpired()
    {
        var token = "avp_" + new string('A', 43);
        _pats.FindByRawTokenAsync(token, Arg.Any<CancellationToken>())
             .Returns(MakePat(token, expiresAt: DateTime.UtcNow.AddMinutes(-1)));

        var result = await Authenticate("Avalon " + token);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Fail_WhenAccountInactive()
    {
        var token = "avp_" + new string('A', 43);
        _pats.FindByRawTokenAsync(token, Arg.Any<CancellationToken>()).Returns(MakePat(token));
        _accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
                 .Returns(MakeAccount(status: AccountStatus.Banned));

        var result = await Authenticate("Avalon " + token);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task Success_WithPatIdClaimAndTokenRoles()
    {
        var token = "avp_" + new string('A', 43);
        _pats.FindByRawTokenAsync(token, Arg.Any<CancellationToken>()).Returns(MakePat(token, AccountAccessLevel.Player));
        _accounts.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(MakeAccount(AccountAccessLevel.Admin));

        var result = await Authenticate("Avalon " + token);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Principal!.Claims, c => c.Type == "pat_id" && c.Value == "5");
        Assert.Contains(result.Principal.Claims, c => c.Type == ClaimTypes.GroupSid && c.Value == "Player");
        // Token roles (Player) NOT account roles (Admin) — crucial.
        Assert.DoesNotContain(result.Principal.Claims, c => c.Type == ClaimTypes.GroupSid && c.Value == "Admin");
    }
}

using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Controllers;

public class PersonalAccessTokenControllerShould
{
    private readonly IPersonalAccessTokenService _service = Substitute.For<IPersonalAccessTokenService>();
    private readonly IAuthorizationService _authz = Substitute.For<IAuthorizationService>();

    private PersonalAccessTokenController MakeSut(ClaimsPrincipal user) =>
        new(_service, _authz)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            }
        };

    private static ClaimsPrincipal User(long accountId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, accountId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new(new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.Role));
    }

    private static ClaimsPrincipal UserWithPatId(long accountId, long patId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, accountId.ToString()),
            new("pat_id", patId.ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new(new ClaimsIdentity(claims, "test", ClaimTypes.NameIdentifier, ClaimTypes.Role));
    }

    private static PersonalAccessToken MakePat(long accountId, uint id = 1) => new()
    {
        Id = new PersonalAccessTokenId(id),
        AccountId = new AccountId(accountId),
        Name = "my-token",
        TokenPrefix = "avp_abcd",
        Roles = AccountAccessLevel.Player,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(30),
    };

    private static MintResult MakeMintResult(uint id = 1) =>
        new(new PersonalAccessTokenId(id), "my-token", "avp_fulltokenvalue", "avp_abcd",
            DateTime.UtcNow.AddDays(365), AccountAccessLevel.Player);

    [Fact]
    public async Task Create_Returns403_WhenCallerIsPat()
    {
        var user = UserWithPatId(7, 42, AvalonRoles.Player);
        var sut = MakeSut(user);

        var result = await sut.Create(new CreatePatRequest { Name = "x" }, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        await _service.DidNotReceive().MintSelfAsync(
            Arg.Any<AccountId>(), Arg.Any<AccountAccessLevel>(), Arg.Any<string>(),
            Arg.Any<DateTime?>(), Arg.Any<AccountAccessLevel?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Delegates_WhenCallerIsJwt()
    {
        var user = User(7, AvalonRoles.Player);
        _service.MintSelfAsync(
            Arg.Any<AccountId>(), Arg.Any<AccountAccessLevel>(), "x",
            Arg.Any<DateTime?>(), Arg.Any<AccountAccessLevel?>(), Arg.Any<CancellationToken>())
            .Returns(MakeMintResult(5));

        var sut = MakeSut(user);
        var result = await sut.Create(new CreatePatRequest { Name = "x" }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<PatCreatedDto>(created.Value);
        Assert.Equal("avp_fulltokenvalue", dto.Token);
        Assert.Equal(5u, dto.Id);
        Assert.Equal(7, dto.AccountId);
        Assert.Equal("avp_abcd", dto.Prefix);
        await _service.Received(1).MintSelfAsync(
            Arg.Is<AccountId>(a => a.Value == 7), Arg.Any<AccountAccessLevel>(), "x",
            Arg.Any<DateTime?>(), Arg.Any<AccountAccessLevel?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_ReturnsOwnTokens()
    {
        var user = User(7, AvalonRoles.Player);
        _service.ListByAccountAsync(Arg.Is<AccountId>(a => a.Value == 7), true, Arg.Any<CancellationToken>())
            .Returns(new List<PersonalAccessToken> { MakePat(7, 1), MakePat(7, 2) });

        var sut = MakeSut(user);
        var result = await sut.List(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Equal(7, p.AccountId));
    }

    [Fact]
    public async Task GetById_Returns404_WhenMissing()
    {
        var user = User(7, AvalonRoles.Player);
        _service.GetAsync(Arg.Any<PersonalAccessTokenId>(), Arg.Any<CancellationToken>())
            .Returns((PersonalAccessToken?)null);

        var sut = MakeSut(user);
        var result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_Returns200_WhenAuthzSucceeds()
    {
        var user = User(7, AvalonRoles.Player);
        var pat = MakePat(7, 42);
        _service.GetAsync(Arg.Any<PersonalAccessTokenId>(), Arg.Any<CancellationToken>()).Returns(pat);
        _authz.AuthorizeAsync(user, pat, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        var sut = MakeSut(user);
        var result = await sut.GetById(42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<PatDto>(ok.Value);
        Assert.Equal(42u, dto.Id);
        Assert.Equal(7, dto.AccountId);
    }

    [Fact]
    public async Task GetById_Returns404_WhenAuthzFailsAndCallerIsPlayer()
    {
        var user = User(7, AvalonRoles.Player);
        var pat = MakePat(99, 42);
        _service.GetAsync(Arg.Any<PersonalAccessTokenId>(), Arg.Any<CancellationToken>()).Returns(pat);
        _authz.AuthorizeAsync(user, pat, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        var sut = MakeSut(user);
        var result = await sut.GetById(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Revoke_Delegates_WhenAuthzSucceeds()
    {
        var user = User(7, AvalonRoles.Player);
        var pat = MakePat(7, 42);
        _service.GetAsync(Arg.Any<PersonalAccessTokenId>(), Arg.Any<CancellationToken>()).Returns(pat);
        _authz.AuthorizeAsync(user, pat, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        var sut = MakeSut(user);
        var result = await sut.Revoke(42, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _service.Received(1).RevokeAsync(pat,
            Arg.Is<AccountId>(a => a.Value == 7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revoke_Returns404_WhenMissing()
    {
        var user = User(7, AvalonRoles.Player);
        _service.GetAsync(Arg.Any<PersonalAccessTokenId>(), Arg.Any<CancellationToken>())
            .Returns((PersonalAccessToken?)null);

        var sut = MakeSut(user);
        var result = await sut.Revoke(42, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        await _service.DidNotReceive().RevokeAsync(
            Arg.Any<PersonalAccessToken>(), Arg.Any<AccountId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAdmin_Returns403_WhenCallerIsPat()
    {
        var user = UserWithPatId(99, 1, AvalonRoles.Admin);
        var sut = MakeSut(user);

        var result = await sut.CreateAdmin(
            new CreateAdminPatRequest { AccountId = 7, Name = "x", Roles = AccountAccessLevel.Player },
            CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        await _service.DidNotReceive().MintAdminAsync(
            Arg.Any<AccountAccessLevel>(), Arg.Any<AccountId>(), Arg.Any<string>(),
            Arg.Any<DateTime?>(), Arg.Any<AccountAccessLevel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAdmin_Delegates_WithTargetAccountId()
    {
        var user = User(99, AvalonRoles.Admin);
        _service.MintAdminAsync(
            Arg.Any<AccountAccessLevel>(), Arg.Is<AccountId>(a => a.Value == 7), "x",
            Arg.Any<DateTime?>(), AccountAccessLevel.Player, Arg.Any<CancellationToken>())
            .Returns(MakeMintResult(10));

        var sut = MakeSut(user);
        var result = await sut.CreateAdmin(
            new CreateAdminPatRequest { AccountId = 7, Name = "x", Roles = AccountAccessLevel.Player },
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var dto = Assert.IsType<PatCreatedDto>(created.Value);
        Assert.Equal(7, dto.AccountId);
        Assert.Equal("avp_fulltokenvalue", dto.Token);
        await _service.Received(1).MintAdminAsync(
            Arg.Any<AccountAccessLevel>(), Arg.Is<AccountId>(a => a.Value == 7), "x",
            Arg.Any<DateTime?>(), AccountAccessLevel.Player, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAllForAccount_DelegatesAndReturnsCount()
    {
        var user = User(99, AvalonRoles.Admin);
        _service.RevokeAllForAccountAsync(
            Arg.Is<AccountId>(a => a.Value == 7),
            Arg.Is<AccountId>(a => a.Value == 99),
            Arg.Any<CancellationToken>())
            .Returns(3);

        var sut = MakeSut(user);
        var result = await sut.RevokeAllForAccount(7, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
        await _service.Received(1).RevokeAllForAccountAsync(
            Arg.Is<AccountId>(a => a.Value == 7),
            Arg.Is<AccountId>(a => a.Value == 99),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAdmin_CallsServiceWithGivenAccountId()
    {
        var user = User(99, AvalonRoles.Admin);
        _service.ListByAccountAsync(Arg.Is<AccountId>(a => a.Value == 7), false, Arg.Any<CancellationToken>())
            .Returns(new List<PersonalAccessToken> { MakePat(7, 1) });

        var sut = MakeSut(user);
        var result = await sut.ListAdmin(7, false, CancellationToken.None);

        Assert.Single(result);
        await _service.Received(1).ListByAccountAsync(
            Arg.Is<AccountId>(a => a.Value == 7), false, Arg.Any<CancellationToken>());
    }
}

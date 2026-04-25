using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Config;
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

public class AccountControllerShould
{
    private readonly IAccountService _accountService = Substitute.For<IAccountService>();
    private readonly IAuthorizationService _authz = Substitute.For<IAuthorizationService>();
    private readonly IAuthContext _authContext = Substitute.For<IAuthContext>();
    private readonly IRefreshTokenService _refreshService = Substitute.For<IRefreshTokenService>();
    private readonly AuthenticationConfig _authConfig = new();

    private AccountController MakeSut(ClaimsPrincipal user) =>
        new(_accountService, _authContext, _authz, _refreshService, _authConfig)
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

    private static Account MakeAccount(long id) => new()
    {
        Id = new AccountId(id),
        Username = "user",
        Salt = new byte[] { 0x1 },
        Verifier = new byte[] { 0x2 },
        Email = "u@example.com",
        JoinDate = DateTime.UtcNow,
    };

    [Fact]
    public async Task FindById_Returns200_WhenCallerIsSelf()
    {
        var user = User(7, AvalonRoles.Player);
        var account = MakeAccount(7);
        _accountService.FindByIdAsync(new AccountId(7), Arg.Any<CancellationToken>()).Returns(account);
        _authz.AuthorizeAsync(user, account, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        var sut = MakeSut(user);
        var result = await sut.FindById(7, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task FindById_Returns404_WhenPlayerAsksForOthersAccount()
    {
        var user = User(7, AvalonRoles.Player);
        var account = MakeAccount(99);
        _accountService.FindByIdAsync(new AccountId(99), Arg.Any<CancellationToken>()).Returns(account);
        _authz.AuthorizeAsync(user, account, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        var sut = MakeSut(user);
        var result = await sut.FindById(99, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task FindById_Returns404_WhenAccountMissing()
    {
        var user = User(7, AvalonRoles.Player);
        _accountService.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns((Account?)null);

        var sut = MakeSut(user);
        var result = await sut.FindById(123, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task FindById_Returns403_WhenAuthzFailsAndCallerIsGameMaster()
    {
        var user = User(99, AvalonRoles.GameMaster);
        var account = MakeAccount(7);
        _accountService.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(account);
        _authz.AuthorizeAsync(user, account, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        var sut = MakeSut(user);
        var result = await sut.FindById(7, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ChangePassword_DelegatesToService()
    {
        var user = User(7, AvalonRoles.Player);
        var sut = MakeSut(user);

        await sut.ChangePassword(
            new AccountPasswordChangeRequest { CurrentPassword = "a", NewPassword = "newstrong1" },
            CancellationToken.None);

        await _accountService.Received(1).ChangePasswordAsync(
            new AccountId(7), "a", "newstrong1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitiateEmailChange_Delegates()
    {
        var user = User(7, AvalonRoles.Player);
        _accountService.InitiateEmailChangeAsync(new AccountId(7), "new@t", Arg.Any<CancellationToken>())
            .Returns("tok");

        var sut = MakeSut(user);
        var result = await sut.InitiateEmailChange(
            new AccountEmailChangeRequest { NewEmail = "new@t" }, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public async Task ConfirmEmailChange_Delegates()
    {
        var user = User(7, AvalonRoles.Player);
        var sut = MakeSut(user);

        var result = await sut.ConfirmEmailChange(
            new AccountEmailConfirmRequest { Token = "tok" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _accountService.Received(1).ConfirmEmailChangeAsync("tok", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateStatus_Delegates()
    {
        var user = User(99, AvalonRoles.Admin);
        var sut = MakeSut(user);

        var result = await sut.UpdateStatus(7,
            new AccountStatusPatchRequest { State = Avalon.Api.Contract.AccountStatus.Banned, Reason = "cheat" },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _accountService.Received(1).UpdateStatusAsync(
            new AccountId(7), Avalon.Api.Contract.AccountStatus.Banned, "cheat", new AccountId(99), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateRoles_Delegates()
    {
        var user = User(99, AvalonRoles.Console);
        var sut = MakeSut(user);

        var result = await sut.UpdateRoles(7,
            new AccountRolesPatchRequest { Roles = Contract.AccountAccessLevel.Player | Contract.AccountAccessLevel.GameMaster },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _accountService.Received(1).UpdateRolesAsync(
            new AccountId(7),
            Contract.AccountAccessLevel.Player | Contract.AccountAccessLevel.GameMaster,
            Arg.Any<CancellationToken>());
    }
}

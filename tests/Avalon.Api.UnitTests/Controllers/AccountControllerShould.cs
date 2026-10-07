using System.Security.Claims;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Hosting.Authentication;
using Avalon.Api.Services;
using Avalon.Api.Services.Email;
using Avalon.Api.UnitTests.Services;
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

    private AccountController MakeSut(ClaimsPrincipal user, IEmailSender? emailSender = null) =>
        new(_accountService, _authContext, _authz, _refreshService, _authConfig, emailSender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user, Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback } }
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
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Account account = MakeAccount(7);
        _accountService.FindByIdAsync(new AccountId(7), Arg.Any<CancellationToken>()).Returns(account);
        _authz.AuthorizeAsync(user, account, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Success());

        AccountController sut = MakeSut(user);
        IActionResult result = await sut.FindById(7, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task FindById_Returns404_WhenPlayerAsksForOthersAccount()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        Account account = MakeAccount(99);
        _accountService.FindByIdAsync(new AccountId(99), Arg.Any<CancellationToken>()).Returns(account);
        _authz.AuthorizeAsync(user, account, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        AccountController sut = MakeSut(user);
        IActionResult result = await sut.FindById(99, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task FindById_Returns404_WhenAccountMissing()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        _accountService.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns((Account?)null);

        AccountController sut = MakeSut(user);
        IActionResult result = await sut.FindById(123, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task FindById_Returns403_WhenAuthzFailsAndCallerIsGameMaster()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.GameMaster);
        Account account = MakeAccount(7);
        _accountService.FindByIdAsync(Arg.Any<AccountId>(), Arg.Any<CancellationToken>()).Returns(account);
        _authz.AuthorizeAsync(user, account, Arg.Any<IEnumerable<IAuthorizationRequirement>>())
              .Returns(AuthorizationResult.Failed());

        AccountController sut = MakeSut(user);
        IActionResult result = await sut.FindById(7, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task ChangePassword_DelegatesToService()
    {
        ClaimsPrincipal user = User(7, AvalonRoles.Player);
        AccountController sut = MakeSut(user);

        await sut.ChangePassword(
            new AccountPasswordChangeRequest { CurrentPassword = TestPasswords.Valid, NewPassword = TestPasswords.Other },
            CancellationToken.None);

        await _accountService.Received(1).ChangePasswordAsync(
            new AccountId(7), TestPasswords.Valid, TestPasswords.Other, Arg.Any<System.Net.IPAddress>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Email change is on only when an email sender is configured (#510). With a sender, the start
    /// answers 202 with no body, so the token cannot come back in the response.
    /// </summary>
    [Fact]
    public async Task Start_an_email_change_with_202_and_no_body_when_a_sender_is_configured()
    {
        AccountController sut = MakeSut(User(7, AvalonRoles.Player), new RecordingEmailSender());

        IActionResult result = await sut.InitiateEmailChange(
            new AccountEmailChangeRequest { NewEmail = "new@avalon.monster", CurrentPassword = TestPasswords.Valid },
            CancellationToken.None);

        AcceptedResult accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Null(accepted.Value);
        await _accountService.Received(1).InitiateEmailChangeAsync(new AccountId(7), "new@avalon.monster",
            TestPasswords.Valid, Arg.Any<System.Net.IPAddress>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_an_email_change_with_204_when_a_sender_is_configured()
    {
        AccountController sut = MakeSut(User(7, AvalonRoles.Player), new RecordingEmailSender());

        IActionResult result = await sut.ConfirmEmailChange(new AccountEmailConfirmRequest { Token = "tok" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _accountService.Received(1).ConfirmEmailChangeAsync("tok", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Owner decision (#503), kept by #510: with no email sender configured (Application:Email:Sender
    /// None, the default) both endpoints answer 501 and never reach the service.
    /// </summary>
    [Fact]
    public async Task Refuse_to_start_an_email_change_with_501()
    {
        AccountController sut = MakeSut(User(7, AvalonRoles.Player));

        IActionResult result = await sut.InitiateEmailChange(
            new AccountEmailChangeRequest { NewEmail = "new@avalon.monster", CurrentPassword = TestPasswords.Valid },
            CancellationToken.None);

        AssertEmailChangeUnavailable(result);
        await _accountService.DidNotReceiveWithAnyArgs().InitiateEmailChangeAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Refuse_to_confirm_an_email_change_with_501()
    {
        AccountController sut = MakeSut(User(7, AvalonRoles.Player));

        IActionResult result = await sut.ConfirmEmailChange(
            new AccountEmailConfirmRequest { Token = "tok" }, CancellationToken.None);

        AssertEmailChangeUnavailable(result);
        await _accountService.DidNotReceiveWithAnyArgs().ConfirmEmailChangeAsync(default!, default);
    }

    private static void AssertEmailChangeUnavailable(IActionResult result)
    {
        ObjectResult refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status501NotImplemented, refused.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(refused.Value);
        Assert.Equal(StatusCodes.Status501NotImplemented, problem.Status);
        Assert.Equal("Email change is unavailable until email delivery exists", problem.Detail);
    }

    [Fact]
    public async Task UpdateStatus_Delegates()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.Admin);
        AccountController sut = MakeSut(user);

        IActionResult result = await sut.UpdateStatus(7,
            new AccountStatusPatchRequest { State = Avalon.Api.Contract.AccountStatus.Banned, Reason = "cheat" },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _accountService.Received(1).UpdateStatusAsync(
            new AccountId(7), Avalon.Api.Contract.AccountStatus.Banned, "cheat", new AccountId(99), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMfa_Returns204_AndNamesTheActingAdmin()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.Admin);
        _accountService.RemoveMfaAsync(new AccountId(7), new AccountId(99), Arg.Any<CancellationToken>()).Returns(true);

        AccountController sut = MakeSut(user);
        IActionResult result = await sut.RemoveMfa(7, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _accountService.Received(1).RemoveMfaAsync(new AccountId(7), new AccountId(99), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMfa_Returns404_WhenAccountMissing()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.Admin);
        _accountService.RemoveMfaAsync(Arg.Any<AccountId>(), Arg.Any<AccountId>(), Arg.Any<CancellationToken>())
            .Returns(false);

        AccountController sut = MakeSut(user);
        IActionResult result = await sut.RemoveMfa(123, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task RemoveMfa_Returns403_WhenAdminTargetsOwnAccount()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.Admin);

        AccountController sut = MakeSut(user);
        IActionResult result = await sut.RemoveMfa(99, CancellationToken.None);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
        await _accountService.DidNotReceiveWithAnyArgs().RemoveMfaAsync(default!, default!, default);
    }

    [Fact]
    public async Task RemoveMfa_Returns204_WhenCalledTwice()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.Admin);
        _accountService.RemoveMfaAsync(new AccountId(7), new AccountId(99), Arg.Any<CancellationToken>()).Returns(true);

        AccountController sut = MakeSut(user);

        Assert.IsType<NoContentResult>(await sut.RemoveMfa(7, CancellationToken.None));
        Assert.IsType<NoContentResult>(await sut.RemoveMfa(7, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRoles_Delegates()
    {
        ClaimsPrincipal user = User(99, AvalonRoles.Console);
        AccountController sut = MakeSut(user);

        IActionResult result = await sut.UpdateRoles(7,
            new AccountRolesPatchRequest { Roles = Contract.AccountAccessLevel.Player | Contract.AccountAccessLevel.GameMaster },
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _accountService.Received(1).UpdateRolesAsync(
            new AccountId(7),
            Contract.AccountAccessLevel.Player | Contract.AccountAccessLevel.GameMaster,
            new AccountId(99),
            Arg.Any<CancellationToken>());
    }
}

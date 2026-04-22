using System.Security.Claims;
using Avalon.Api.Authentication;
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

    private AccountController MakeSut(ClaimsPrincipal user) =>
        new(_accountService, _authContext, _authz)
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
}

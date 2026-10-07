using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Controllers;
using Avalon.Api.Exceptions;
using Avalon.Api.UnitTests.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Avalon.Api.UnitTests.Controllers;

public sealed class AccountEmailVerificationControllerShould
{
    [Fact]
    public async Task UsesAuthenticatedAccountAndReturnsNoProof()
    {
        using var f = await AccountEmailVerificationServiceShould.Fixture.Create();
        var controller = Controller(f, f.Account.Id.Value);
        var result = Assert.IsType<AcceptedResult>(await controller.Request(default));
        Assert.Null(result.Value);
        var status = await controller.Get(default);
        Assert.Null(status.EmailVerifiedAt);
        Assert.Equal(f.Now.AddSeconds(60), status.ResendAvailableAt);
        var request = new AccountEmailVerificationConfirmRequest { Token = f.Mail.Token };
        var other = Controller(f, f.Account.Id.Value + 100);
        await Assert.ThrowsAsync<BusinessException>(() => other.Confirm(request, default));
        Assert.IsType<NoContentResult>(await controller.Confirm(request, default));
        Assert.NotNull((await controller.Get(default)).EmailVerifiedAt);
    }

    [Fact]
    public void RoutesRequirePlayerPolicyAndAcceptOnlyBoundedProof()
    {
        var policy = Assert.Single(typeof(AccountEmailVerificationController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AvalonRoles.Player, policy.Policy);
        Assert.Empty(typeof(AccountEmailVerificationController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        foreach (var token in new[] { "", new string('a', 42), new string('a', 44), new string('a', 42) + "!" })
        {
            var request = new AccountEmailVerificationConfirmRequest { Token = token };
            Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
        }
        var valid = new AccountEmailVerificationConfirmRequest { Token = new string('a', 43) };
        Assert.True(Validator.TryValidateObject(valid, new ValidationContext(valid), [], true));
    }

    private static AccountEmailVerificationController Controller(AccountEmailVerificationServiceShould.Fixture f, long id) =>
        new(f.Service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test")),
                    Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback },
                }
            }
        };
}

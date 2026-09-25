using System.Security.Authentication;
using System.Security.Claims;
using Avalon.Api.Authentication;
using Avalon.Api.Services;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.Authentication;

/// <summary>
/// #480: when authentication did not leave its account behind (a principal from some other
/// scheme), the default policy's handler loads the account itself, and must apply the same
/// Active check authentication would have.
/// </summary>
public class AvalonAuthHandlerShould
{
    private readonly IAccountService _accounts = Substitute.For<IAccountService>();

    private async Task<AuthorizationHandlerContext> AuthorizeAsync()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAuthContext, AuthContext>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "7")], "other"));
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = user };
        var accessor = new HttpContextAccessor { HttpContext = http };
        var handler = new AvalonAuthHandler(NullLoggerFactory.Instance, accessor, _accounts);
        var context = new AuthorizationHandlerContext([new AvalonAuthRequirement()], user, null);

        try
        {
            await handler.HandleAsync(context);
        }
        catch (AuthenticationException)
        {
            // Refusal is signalled by throwing, which the middleware answers with 401.
        }

        return context;
    }

    private void AccountIs(AccountStatus status) =>
        _accounts.FindByIdAsync(Arg.Is<AccountId>(id => id.Value == 7), Arg.Any<CancellationToken>())
            .Returns(new Account
            {
                Id = new AccountId(7), Username = "CALLER", Email = "c@avalon.monster",
                Salt = [1], Verifier = [2], JoinDate = DateTime.UtcNow, Status = status,
            });

    [Fact]
    public async Task Admit_an_active_account_it_had_to_load_itself()
    {
        AccountIs(AccountStatus.Active);

        AuthorizationHandlerContext context = await AuthorizeAsync();

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData(AccountStatus.Banned)]
    [InlineData(AccountStatus.Deactivated)]
    public async Task Refuse_an_inactive_account_it_had_to_load_itself(AccountStatus status)
    {
        AccountIs(status);

        AuthorizationHandlerContext context = await AuthorizeAsync();

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }
}

using System.Security.Claims;
using Avalon.Api.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Avalon.Api.UnitTests.GameAuth;

public sealed class GameWorkloadMiddlewareShould
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Establish_only_the_verified_tls_identity_before_the_next_middleware(bool accepted)
    {
        var auth = Substitute.For<IAuthenticationService>();
        using var services = new ServiceCollection().AddSingleton(auth).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.User = new(new ClaimsIdentity([new Claim(GameServerAuthHandler.ServerIdClaim, "forged-player")], "Bearer"));
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new AuthorizeAttribute { AuthenticationSchemes = GameServerAuthHandler.Scheme }), "internal"));
        var workload = new ClaimsPrincipal(new ClaimsIdentity([new Claim(GameServerAuthHandler.ServerIdClaim, "world-1")], GameServerAuthHandler.Scheme));
        auth.AuthenticateAsync(context, GameServerAuthHandler.Scheme).Returns(accepted
            ? AuthenticateResult.Success(new AuthenticationTicket(workload, GameServerAuthHandler.Scheme)) : AuthenticateResult.Fail("untrusted"));
        var app = new ApplicationBuilder(services);
        app.UseGameWorkloadAuthentication();
        app.Run(http => {
            if (accepted) Assert.Same(workload, http.User);
            else Assert.False(http.User.Identity?.IsAuthenticated == true);
            Assert.DoesNotContain(http.User.Claims, claim => claim.Value == "forged-player");
            return Task.CompletedTask;
        });
        await app.Build()(context);
        await auth.Received(1).AuthenticateAsync(context, GameServerAuthHandler.Scheme);
    }
}

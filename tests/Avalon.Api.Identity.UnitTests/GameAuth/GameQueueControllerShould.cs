using System.Diagnostics.Metrics;
using Avalon.Api.Contract;
using Avalon.Api.Hosting.Config;
using Avalon.Api.Hosting.Middlewares;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Controllers;
using Avalon.Api.Identity.Services;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameAuth;
using Avalon.Infrastructure.WorldPlaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

/// <summary>#967: what a waiter is told at each check-in, and when to check in again. The line's rules are the scenario suite's.</summary>
public sealed class GameQueueControllerShould
{
    [Theory]
    [InlineData("front", true, "queued", 1, 5)]
    [InlineData("back", true, "queued", 11, 15)]
    [InlineData("front", false, "paused", 1, 5)]
    [InlineData("turn", true, "your_turn", null, null)]
    [InlineData("none", true, "not_queued", null, null)]
    public async Task Answer_each_state_with_when_to_check_in_again(string line, bool ready, string state, int? position, int? after)
    {
        var h = new JoinHarness();
        DateTime until = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(45);
        await h.Places.RenewAsync(1, 8, 1, Guid.NewGuid(), until, false, default); // account 8's session holds the one place
        int ahead = line == "back" ? 10 : 0;
        for (int waiter = 0; waiter < ahead; waiter++)
            await h.Places.EnterAsync(new PlaceRequest(1, 100 + waiter, until, 1, false, 5000), default);
        if (line != "none") await h.Places.EnterAsync(new PlaceRequest(1, 7, until, 1, false, 5000), default);
        if (line == "turn") await h.Places.EndAsync(1, 8, 1, PlaceEnd.Remove, until, default);
        GameAuthReply auth = await h.Authenticate();

        OkObjectResult result = Assert.IsType<OkObjectResult>(await Controller(h, ready).Status(
            new GameQueueRequest { GameContextCredential = auth.GameContextCredential!, WorldId = 1 }, default));

        GameQueueStatusReply reply = Assert.IsType<GameQueueStatusReply>(result.Value);
        Assert.Equal((state, position, after, (int?)null), (reply.State, reply.Position, reply.CheckInAfterSeconds, reply.EstimatedWaitSeconds));
    }

    /// <summary>A signed-out context's credential, still within its 5 minutes, checks in no more.</summary>
    [Fact]
    public async Task Refuse_the_credential_of_a_revoked_context()
    {
        var h = new JoinHarness();
        DateTime until = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(45);
        await h.Places.RenewAsync(1, 8, 1, Guid.NewGuid(), until, false, default);
        await h.Places.EnterAsync(new PlaceRequest(1, 7, until, 1, false, 5000), default); // account 7 waits
        GameAuthReply auth = await h.Authenticate();
        await h.Authorization.LogoutAsync(auth.GameContextCredential!, default);

        IActionResult result = await Controller(h, true).Status(
            new GameQueueRequest { GameContextCredential = auth.GameContextCredential!, WorldId = 1 }, default);

        Assert.Equal("AUTHORIZATION_REQUIRED", Assert.IsType<GameQueueStatusReply>(Assert.IsType<UnauthorizedObjectResult>(result).Value).Error);
    }

    private static GameQueueController Controller(JoinHarness h, bool ready)
    {
        IWorldRepository worlds = Substitute.For<IWorldRepository>();
        worlds.FindByIdAsync(new WorldId(1), false, Arg.Any<CancellationToken>()).Returns(new Avalon.Domain.Auth.World
        {
            Id = new WorldId(1),
            Name = "Avalon",
            Host = "localhost",
            Port = 21000,
            MinVersion = "0.0.1",
            Version = "0.0.1",
            PlayerLimit = 1,
        });
        IWorldReadiness readiness = Substitute.For<IWorldReadiness>();
        readiness.IsReadyAsync(1, Arg.Any<CancellationToken>()).Returns(ready);
        var queue = new GameQueueService(h.Places, new GameWorldAvailability(worlds, readiness, h.Clock), Options.Create(h.PlaceConfiguration),
            new WorldPlaceMetrics(new Meter("test-queue")));
        var limiter = new QueueRateLimiter(Options.Create(new RateLimitingConfig()), Substitute.For<IExemptSources>(), h.Clock);
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        return new GameQueueController(NullLogger<GameQueueController>.Instance, h.Authorization, queue, limiter)
        {
            ControllerContext = new() { HttpContext = http },
        };
    }
}

using System.Reflection;
using System.Text.Json;
using Avalon.Api.Contract;
using Avalon.Api.Identity.Authentication;
using Avalon.Api.Identity.Controllers;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using AccountStatus = Avalon.Domain.Auth.AccountStatus;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class GameAdmissionControllerShould
{
    [Fact]
    public void Reject_body_selected_account_session_fence_and_server_identity()
    {
        string credential = new string('A', 43);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<GameJoinRequest>("{\"GameContextCredential\":\"" + credential + "\",\"WorldId\":1,\"AccountId\":\"1\"}"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<JoinRedemptionRequest>("{\"JoinTicket\":\"" + credential + "\",\"ServerId\":\"world-other\"}"));
    }
    [Fact]
    public void Require_the_dedicated_workload_scheme_and_bound_noncacheable_requests()
    {
        Type type = typeof(InternalGameAdmissionController);
        Assert.Equal(GameServerAuthHandler.Scheme, type.GetCustomAttribute<AuthorizeAttribute>()!.AuthenticationSchemes);
        Assert.Null(type.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.True(type.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        Assert.Equal(4096, ((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)type.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize);
    }
    [Fact]
    public async Task Reject_plain_http_before_ticket_creation_or_world_listing()
    {
        var h = new JoinHarness();
        var controller = new GameAdmissionController(NullLogger<GameAdmissionController>.Instance, h.Authorization, h.Tickets, h.Allocator, new GameApplicationAccessPolicy(Options.Create(h.Configuration))) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<BadRequestObjectResult>(await controller.Join(new GameJoinRequest { GameContextCredential = new string('A', 43), WorldId = 1 }, Guid.NewGuid(), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.Worlds(new GameContextCredentialRequest { GameContextCredential = new string('A', 43) }, CancellationToken.None));
        Assert.Empty(h.Store.Entries);
    }

    public enum WorldRefusal { Unavailable, ClientOutdated, BelowApiFloor, NotAllowed, Banned, CharacterGone }

    /// <summary>
    /// A world refusal says whether a retry may change it (#1053), on the REST join and reconnect tickets, at the
    /// redemption the world asks for a ticket issued before the change, and in the world list: only "try later" is 503
    /// (the world down, in maintenance, without its database); a client too old for the world is 426 with the version it
    /// needs, written as compared, and the world stays listed, marked; one below the API's floor, a context not allowed
    /// on the world, an account that may not play and a character not the account's are refused for good.
    /// </summary>
    [Theory]
    [InlineData(WorldRefusal.Unavailable, 503, "WORLD_UNAVAILABLE", null, "WORLD_UNAVAILABLE", "hidden")]
    [InlineData(WorldRefusal.ClientOutdated, 426, "CLIENT_OUTDATED", "1.0.0", "CLIENT_OUTDATED", "update")]
    [InlineData(WorldRefusal.BelowApiFloor, 400, "UNSUPPORTED_PROTOCOL", null, "UNSUPPORTED_PROTOCOL", "hidden")]
    [InlineData(WorldRefusal.NotAllowed, 403, "WORLD_NOT_ALLOWED", null, "WORLD_NOT_ALLOWED", "hidden")]
    [InlineData(WorldRefusal.Banned, 403, "ACCOUNT_BANNED", null, "CONTEXT_REVOKED", "refused")]
    [InlineData(WorldRefusal.CharacterGone, 404, "CHARACTER_NOT_FOUND", null, "CHARACTER_NOT_FOUND", "listed")]
    public async Task Tell_a_retryable_world_refusal_from_a_permanent_one(WorldRefusal refusal, int status, string error,
        string? requiredVersion, string redemption, string listing)
    {
        var h = new GameAdmissionHarness(false);
        h.Characters.FindByIdAndAccountAsync(new CharacterId(42), h.Account.Id, Arg.Any<CancellationToken>()).Returns(new Character());
        string credential = (await h.Authenticate(2514590)).GameContextCredential!;
        GameJoinReply issued = await h.Tickets.IssueAsync(credential, 3, 42, Guid.NewGuid(), false, false, default);
        Assert.Null(issued.Error);

        switch (refusal)
        {
            case WorldRefusal.Unavailable: h.Readiness.IsReadyAsync(3, Arg.Any<CancellationToken>()).Returns(false); break;
            case WorldRefusal.ClientOutdated: h.World.MinVersion = "01.0.0"; break;
            case WorldRefusal.BelowApiFloor: h.Workloads.MinClientVersion = "0.4.0"; break;
            case WorldRefusal.NotAllowed: h.World.AccessLevelRequired = AccountAccessLevel.GameMaster; break;
            case WorldRefusal.Banned: h.Account.Status = AccountStatus.Banned; break;
            default:
                h.Characters.FindByIdAndAccountAsync(new CharacterId(42), h.Account.Id, Arg.Any<CancellationToken>()).Returns((Character?)null);
                break;
        }

        foreach (bool reconnect in new[] { false, true })
        {
            ObjectResult answer = Assert.IsAssignableFrom<ObjectResult>(await h.Join(credential, 42, reconnect));
            GameJoinReply reply = Assert.IsType<GameJoinReply>(answer.Value);
            Assert.Equal(status, answer.StatusCode);
            Assert.Equal(error, reply.Error);
            Assert.Equal(requiredVersion, reply.RequiredVersion);
            Assert.Null(reply.JoinTicket);
        }
        Assert.Equal(redemption, (await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default)).Error);

        IActionResult listed = await h.Worlds(credential);
        if (listing == "refused")
        {
            Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<ObjectResult>(listed).StatusCode);
            return;
        }
        IReadOnlyList<GameWorldDestination> worlds = Assert.IsAssignableFrom<IReadOnlyList<GameWorldDestination>>(Assert.IsType<OkObjectResult>(listed).Value);
        if (listing == "hidden")
        {
            Assert.Empty(worlds);
            return;
        }
        GameWorldDestination world = Assert.Single(worlds);
        Assert.Equal(listing == "update", world.RequiresUpdate);
        Assert.Equal(h.World.MinVersion, world.MinVersion);
    }
}

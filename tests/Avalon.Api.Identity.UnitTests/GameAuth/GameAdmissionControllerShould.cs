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
using WorldId = Avalon.Domain.Auth.WorldId;

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
            // The 426's advisory Upgrade header names the same version, as a product token.
            Assert.Equal(requiredVersion is null ? string.Empty : "avalon-client/" + requiredVersion, h.LastJoin!.Response.Headers.Upgrade.ToString());
            Assert.Null(reply.JoinTicket);
        }
        // The world's redemption of the ticket issued before: 503 only for "try later"; every other refusal is 403, 401
        // being the workload's own (#1053).
        ObjectResult redeemed = Assert.IsAssignableFrom<ObjectResult>(await h.Redeem(issued.JoinTicket!));
        Assert.Equal(redemption, Assert.IsType<JoinRedemptionReceipt>(redeemed.Value).Error);
        Assert.Equal(redemption == "WORLD_UNAVAILABLE" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden, redeemed.StatusCode);

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

    /// <summary>
    /// A world the context may not enter is never told apart from one that does not exist (#1053, the rule
    /// <c>WorldRouteMiddleware</c> keeps): an id with no row, a row with no workload, and a world above the account's
    /// access level, its database up or down, are one 403 <c>WORLD_NOT_ALLOWED</c>, and none of them reads the world's
    /// database or readiness, whose answer would tell a restricted world's state.
    /// </summary>
    [Theory]
    [InlineData((ushort)9, true)]
    [InlineData((ushort)5, true)]
    [InlineData((ushort)3, true)]
    [InlineData((ushort)3, false)]
    public async Task Refuse_a_world_the_context_may_not_enter_as_one_that_does_not_exist(ushort worldId, bool databaseUp)
    {
        var h = new GameAdmissionHarness(false);
        // World 5: a row any Player may enter, but no workload. World 3 is PTR-only, and this context is the main
        // application's, which does not lift the account to PTR. World 9 has no row.
        var unassigned = new Avalon.Domain.Auth.World { Id = new WorldId(5), Name = "Unassigned", Host = "five.example.test", Port = 21000, MinVersion = "0.2.0", Version = "0.2.0" };
        h.WorldRows.FindByIdAsync(unassigned.Id, false, Arg.Any<CancellationToken>()).Returns(unassigned);
        h.Databases.IsAvailable(Arg.Any<WorldId>()).Returns(databaseUp);
        h.Databases.ClearReceivedCalls();
        h.Readiness.ClearReceivedCalls();
        string credential = (await h.Authenticate(2499460)).GameContextCredential!;

        ObjectResult answer = Assert.IsAssignableFrom<ObjectResult>(await h.Join(credential, null, false, worldId));
        Assert.Equal(StatusCodes.Status403Forbidden, answer.StatusCode);
        Assert.Equal("WORLD_NOT_ALLOWED", Assert.IsType<GameJoinReply>(answer.Value).Error);
        await h.WorldRows.Received(1).FindByIdAsync(new WorldId(worldId), false, Arg.Any<CancellationToken>());
        // The world list hides it too, its database up or down (world 3 is the only one with a workload).
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<GameWorldDestination>>(Assert.IsType<OkObjectResult>(await h.Worlds(credential)).Value));
        h.Databases.DidNotReceive().IsAvailable(Arg.Any<WorldId>());
        await h.Readiness.DidNotReceive().IsReadyAsync(Arg.Any<ushort>(), Arg.Any<CancellationToken>());
    }
}

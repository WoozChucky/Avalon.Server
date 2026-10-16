using Avalon.Api.Services;
using Avalon.Common.Accounts;
using Avalon.Common.GameAuth;
using Avalon.Infrastructure.GameAuth;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.GameAuth;

public sealed class PlaytestWorldAdmissionShould
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admit_and_reconnect_a_linked_or_new_Player_to_a_PTR_world_without_promoting_the_account(bool createAccount)
    {
        var h = new GameAdmissionHarness(createAccount);
        GameAuthReply auth = await h.Authenticate(2514590);
        GameContextRecord context = Assert.IsType<GameContextRecord>(await h.Authorization.GetContextAsync(auth.GameContextCredential!, true, default));
        Assert.Equal((ushort)3, Assert.Single(await h.Allocator.ListAsync(context, default)).WorldId);
        OkObjectResult listed = Assert.IsType<OkObjectResult>(await h.Worlds(auth.GameContextCredential!));
        Assert.Equal((ushort)3, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<GameWorldDestination>>(listed.Value)).WorldId);
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);

        GameJoinReply issued = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, false, default);
        Assert.Null(issued.Error);
        JoinRedemptionReceipt admitted = await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Null(admitted.Error);
        Assert.Equal(context.Id.ToString("D"), admitted.GameContextId);
        Assert.Equal("1", admitted.FencingToken);
        GameSessionLeaseReply lease = await h.Activate();
        Assert.Null(lease.Error);
        Assert.Equal((ushort?)1, lease.AccessLevel);
        Assert.Equal((ushort?)3, lease.WorldId);
        Assert.Equal((ushort?)1, (await h.Heartbeat()).AccessLevel);

        GameJoinReply reconnect = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default);
        Assert.Null(reconnect.Error);
        JoinRedemptionReceipt rejoined = await h.Tickets.RedeemAsync(reconnect.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default);
        Assert.Null(rejoined.Error);
        Assert.Equal(context.Id.ToString("D"), rejoined.GameContextId);
        Assert.Equal("2", rejoined.FencingToken);
        Assert.NotEqual(admitted.GameSessionId, rejoined.GameSessionId);
        Assert.Equal((ushort?)1, (await h.Activate()).AccessLevel);
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);
    }

    [Theory]
    [InlineData(2514590u, false)]
    [InlineData(2514590u, true)]
    [InlineData(2499460u, false)]
    [InlineData(2499460u, true)]
    public async Task Require_current_Playtest_ownership_to_list_PTR_without_changing_main_context_listing(uint appId, bool expired)
    {
        var h = new GameAdmissionHarness(false) { OwnsApplication = expired, LicenseLifetime = TimeSpan.FromSeconds(30) };
        if (appId == 2499460) h.Account.AccessLevel |= AccountAccessLevel.PTR;
        GameAuthReply auth = await h.Authenticate(appId, expired ? GameAuthStates.Authorized : GameAuthStates.PendingLicense);
        if (expired) h.Advance(TimeSpan.FromSeconds(31));
        Assert.NotNull(await h.Authorization.GetContextAsync(auth.GameContextCredential!, false, default));
        Assert.Null(await h.Authorization.GetContextAsync(auth.GameContextCredential!, true, default));

        IActionResult result = await h.Worlds(auth.GameContextCredential!);
        if (appId == 2514590)
        {
            Assert.IsType<UnauthorizedObjectResult>(result);
        }
        else
        {
            OkObjectResult listed = Assert.IsType<OkObjectResult>(result);
            Assert.Equal((ushort)3, Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<GameWorldDestination>>(listed.Value)).WorldId);
        }
    }

    [Fact]
    public async Task Keep_the_same_Players_main_application_context_out_of_PTR()
    {
        var h = new GameAdmissionHarness(false);
        GameAuthReply playtest = await h.Authenticate(2514590);
        GameContextRecord playtestContext = Assert.IsType<GameContextRecord>(await h.Authorization.GetContextAsync(playtest.GameContextCredential!, true, default));
        Assert.Equal((ushort)3, Assert.Single(await h.Allocator.ListAsync(playtestContext, default)).WorldId);

        GameAuthReply main = await h.Authenticate(2499460);
        GameContextRecord mainContext = Assert.IsType<GameContextRecord>(await h.Authorization.GetContextAsync(main.GameContextCredential!, true, default));
        Assert.Equal(playtestContext.AccountId, mainContext.AccountId);
        Assert.Empty(await h.Allocator.ListAsync(mainContext, default));
        GameJoinReply denied = await h.Tickets.IssueAsync(main.GameContextCredential!, 3, null, Guid.NewGuid(), false, false, default);
        Assert.Equal(GameAuthErrors.WorldNotAllowed, denied.Error);
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);

        // A ticket issued while the application was allowed on the world, and redeemed once it no longer is, is refused
        // for good, never as the "try later" the world answers WORLD_UNAVAILABLE with (#1050).
        GameJoinReply issued = await h.Tickets.IssueAsync(playtest.GameContextCredential!, 3, null, Guid.NewGuid(), false, false, default);
        Assert.Null(issued.Error);
        h.Configuration.SteamPlaytest.AllowedWorldIds = [4];
        Assert.Equal(GameAuthErrors.WorldNotAllowed,
            (await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default)).Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refuse_fresh_and_cached_admission_and_heartbeat_when_Playtest_is_disabled_or_ownership_is_lost(bool disableApplication)
    {
        var h = new GameAdmissionHarness(false);
        GameAuthReply auth = await h.Authenticate(2514590);
        var request = Guid.NewGuid();
        GameJoinReply issued = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default);
        Assert.Null(issued.Error);
        var connection = Guid.NewGuid();
        var redemption = Guid.NewGuid();
        JoinRedemptionReceipt admitted = await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default);
        Assert.Null(admitted.Error);
        Assert.Null((await h.Activate()).Error);
        Assert.Null((await h.Heartbeat()).Error);
        GameJoinReply reconnect = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default);
        Assert.Null(reconnect.Error);

        if (disableApplication) h.Configuration.SteamPlaytest.Enabled = false;
        else await h.RevokePlaytestAuthority();

        Assert.NotNull((await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default)).Error);
        Assert.NotNull((await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default)).Error);
        Assert.NotNull((await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default)).Error);
        Assert.NotNull((await h.Tickets.RedeemAsync(reconnect.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default)).Error);
        Assert.NotNull((await h.Heartbeat()).Error);
        Assert.NotNull((await h.Activate()).Error);
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);
    }

    [Fact]
    public async Task Suspension_refuses_fresh_Steam_proof_cached_admission_and_session_renewal()
    {
        var h = new GameAdmissionHarness(false);
        GameAuthReply auth = await h.Authenticate(2514590);
        var request = Guid.NewGuid();
        GameJoinReply issued = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default);
        Assert.Null(issued.Error);
        var connection = Guid.NewGuid();
        var redemption = Guid.NewGuid();
        Assert.Null((await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default)).Error);
        Assert.Null((await h.Activate()).Error);
        Assert.Null((await h.Heartbeat()).Error);
        GameJoinReply reconnect = await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default);
        Assert.Null(reconnect.Error);

        h.SetPlaytestSuspension(true);
        await h.Authenticate(2514590, GameAuthStates.PendingLicense);
        Assert.NotNull((await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, request, false, false, default)).Error);
        Assert.NotNull((await h.Tickets.IssueAsync(auth.GameContextCredential!, 3, null, Guid.NewGuid(), false, true, default)).Error);
        Assert.NotNull((await h.Tickets.RedeemAsync(issued.JoinTicket!, "world-3", connection, redemption, default)).Error);
        Assert.NotNull((await h.Tickets.RedeemAsync(reconnect.JoinTicket!, "world-3", Guid.NewGuid(), Guid.NewGuid(), default)).Error);
        Assert.NotNull((await h.Heartbeat()).Error);
        Assert.NotNull((await h.Activate()).Error);

        h.SetPlaytestSuspension(false);
        Assert.Null(await h.Authorization.GetContextAsync(auth.GameContextCredential!, true, default));
        GameAuthReply fresh = await h.Authenticate(2514590);
        Assert.NotNull(await h.Authorization.GetContextAsync(fresh.GameContextCredential!, true, default));
        Assert.Equal(AccountAccessLevel.Player, h.Account.AccessLevel);
    }
}

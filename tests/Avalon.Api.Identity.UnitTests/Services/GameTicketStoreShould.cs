using System.Collections.Concurrent;
using Avalon.Common.ValueObjects;
using Avalon.Infrastructure;
using Avalon.Infrastructure.GameTickets;
using NSubstitute;
using Xunit;

namespace Avalon.Api.Identity.UnitTests.Services;

public sealed class GameTicketStoreShould
{
    [Fact]
    public async Task Issue_a_hashed_single_use_ticket_for_sixty_seconds()
    {
        var entries = new ConcurrentDictionary<string, string>();
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        TimeSpan? lifetime = null;
        cache.SetNxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(call =>
            {
                lifetime = call.ArgAt<TimeSpan>(2);
                return entries.TryAdd(call.ArgAt<string>(0), call.ArgAt<string>(1));
            });
        cache.TakeAsync(Arg.Any<string>()).Returns(call =>
            entries.TryRemove(call.ArgAt<string>(0), out string? value) ? value : null);
        var sut = new RedisGameTicketStore(cache);
        var grant = new GameTicketGrant(new AccountId(7L), Guid.Parse("12345678-1234-1234-1234-123456789abc"), 3);

        string ticket = await sut.IssueAsync(grant, CancellationToken.None);

        Assert.Equal(43, ticket.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", ticket);
        Assert.Equal(TimeSpan.FromSeconds(60), lifetime);
        Assert.Single(entries);
        Assert.DoesNotContain(ticket, entries.Single().Key);
        Assert.DoesNotContain(ticket, entries.Single().Value);
        Assert.Equal(grant, await sut.RedeemAsync(ticket, CancellationToken.None));
        Assert.Null(await sut.RedeemAsync(ticket, CancellationToken.None));
    }

    [Fact]
    public async Task Give_only_one_of_two_concurrent_redeemers_the_grant()
    {
        var entries = new ConcurrentDictionary<string, string>();
        IReplicatedCache cache = Substitute.For<IReplicatedCache>();
        cache.SetNxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(call => entries.TryAdd(call.ArgAt<string>(0), call.ArgAt<string>(1)));
        cache.TakeAsync(Arg.Any<string>()).Returns(call =>
            entries.TryRemove(call.ArgAt<string>(0), out string? value) ? value : null);
        var sut = new RedisGameTicketStore(cache);
        string ticket = await sut.IssueAsync(new GameTicketGrant(new AccountId(7L), Guid.NewGuid(), 3), CancellationToken.None);

        GameTicketGrant?[] answers = await Task.WhenAll(sut.RedeemAsync(ticket, CancellationToken.None),
            sut.RedeemAsync(ticket, CancellationToken.None));

        Assert.Single(answers, answer => answer is not null);
    }
}

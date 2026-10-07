using Avalon.Common.Accounts;
using Avalon.Database.Auth.Repositories;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps;
using Avalon.World.Parties;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Chat;

/// <summary>A chat limiter for tests that are about something else: the limit is off.</summary>
internal static class ChatLimits
{
    public static ChatRateLimiter Off() => new(Options.Create(new GameConfiguration()), TimeProvider.System);
}

/// <summary>
/// The chat rate limit (#722): one sliding 60 s budget per sender, shared by plain chat, /p and /w, spent only by
/// delivered messages, checked before anything is looked up.
/// </summary>
public class ChatRateLimitShould
{
    private const int Budget = 3;

    private readonly FakeTimeProvider _time = new();
    private readonly GameConfiguration _config = new() { ChatMessagesPerMinute = Budget };
    private readonly PartyTestWorld _w = new();
    private readonly ChatRateLimiter _limiter;
    private readonly ChatMessageHandler _handler;

    public ChatRateLimitShould()
    {
        _limiter = new ChatRateLimiter(Options.Create(_config), _time);
        var commands = new ICommand[]
        {
            new WhisperCommand(_w.Parties.Online, _limiter),
            new PartyChatCommand(_w.Parties, _limiter),
            new InviteCommand(_w.Parties),
            new LeaveCommand(_w.Parties),
        };
        _handler = new ChatMessageHandler(MapInstanceClients.NewWorld(),
            new CommandDispatcher(commands, NullLogger<CommandDispatcher>.Instance), _limiter);
    }

    private void Send(PartyClient from, string message)
    {
        from.Connection.AccessLevel.Returns(AccessLevels.Player);
        _handler.Execute(from.Connection, new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow });
    }

    private static List<SChatMessagePacket> Heard(PartyClient c, ChatChannel channel) =>
        c.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE).Where(m => m.Channel == channel).ToList();

    private static string TooFast(int seconds) => $"You're sending messages too fast. Try again in {seconds}s.";

    [Fact]
    public void Deliver_the_budget_then_refuse_the_next_with_the_wait_rounded_up()
    {
        PartyClient a = _w.Online(1, "Aren");

        for (int i = 0; i < Budget; i++)
        {
            Send(a, $"hi {i}");
            _time.Advance(TimeSpan.FromSeconds(10)); // 30 s in
        }

        Assert.Equal(Budget, Heard(a, ChatChannel.Say).Count);
        a.Clear();
        _time.Advance(TimeSpan.FromMilliseconds(500)); // oldest sent 30.5 s ago: 29.5 s to go, rounded up to 30
        Send(a, "one too many");

        Assert.Equal([TooFast(30)], a.Lines());
        Assert.Empty(Heard(a, ChatChannel.Say));
    }

    [Fact]
    public void Allow_again_once_the_oldest_message_leaves_the_window()
    {
        PartyClient a = _w.Online(1, "Aren");
        Send(a, "1");
        _time.Advance(TimeSpan.FromSeconds(20));
        Send(a, "2");
        Send(a, "3");
        a.Clear();

        _time.Advance(TimeSpan.FromSeconds(39));
        Send(a, "too early"); // the oldest is 59 s old
        Assert.Equal([TooFast(1)], a.Lines());

        a.Clear();
        _time.Advance(TimeSpan.FromSeconds(1)); // 60 s: the oldest has left, the other two have not
        Send(a, "again");

        Assert.Empty(a.Lines());
        Assert.Equal("again", Assert.Single(Heard(a, ChatChannel.Say)).Message);
        Send(a, "and over");
        Assert.Equal([TooFast(20)], a.Lines()); // "2", "3" and "again" fill it
    }

    [Fact]
    public void Share_one_budget_across_plain_party_and_whisper_messages()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");
        _w.Form(a, b);

        Send(a, "plain");
        Send(a, "/p party");
        Send(a, "/w Kaela whisper");
        a.Clear();
        b.Clear();

        Send(a, "plain again");
        Send(a, "/p party again");
        Send(a, "/w Kaela whisper again");

        Assert.Equal([TooFast(60), TooFast(60), TooFast(60)], a.Lines());
        Assert.Empty(b.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
    }

    [Fact]
    public void Keep_one_senders_budget_from_another_senders()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");
        for (int i = 0; i < Budget; i++)
            Send(a, "x");

        Send(b, "mine");

        Assert.Empty(b.Lines());
        Assert.Single(Heard(b, ChatChannel.Say));
    }

    [Fact]
    public void Not_spend_budget_on_a_whisper_to_nobody_a_usage_error_or_a_self_whisper()
    {
        PartyClient a = _w.Online(1, "Aren");
        _w.Online(2, "Kaela");

        for (int i = 0; i < 2 * Budget; i++)
        {
            Send(a, "/w Nobody hello");
            Send(a, "/w Kaela");
            Send(a, "/w Aren me");
        }

        a.Clear();
        for (int i = 0; i < Budget; i++)
            Send(a, "/w Kaela hello");

        Assert.Empty(a.Lines());
        Assert.Equal(Budget, Heard(a, ChatChannel.Whisper).Count);
    }

    [Fact]
    public void Not_spend_budget_on_party_chat_while_not_in_a_party_or_with_no_text()
    {
        PartyClient a = _w.Online(1, "Aren");
        for (int i = 0; i < 2 * Budget; i++)
        {
            Send(a, "/p hello");
            Send(a, "/p");
        }

        PartyClient b = _w.Online(2, "Kaela");
        _w.Form(a, b);
        a.Clear();
        for (int i = 0; i < Budget; i++)
            Send(a, "/p hello");

        Assert.Empty(a.Lines());
        Assert.Equal(Budget, Heard(b, ChatChannel.Party).Count);
    }

    [Fact]
    public void Tell_a_sender_over_the_limit_nothing_about_who_is_online()
    {
        PartyClient a = _w.Online(1, "Aren");
        _w.Online(2, "Kaela");
        for (int i = 0; i < Budget; i++)
            Send(a, "x");
        a.Clear();

        Send(a, "/w Kaela hello");
        string forOnline = Assert.Single(a.Lines());
        a.Clear();
        Send(a, "/w Nobody hello");

        Assert.Equal(forOnline, Assert.Single(a.Lines()));
        Assert.Contains("too fast", forOnline, StringComparison.Ordinal);
    }

    [Fact]
    public void Leave_other_commands_alone_and_uncounted()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");
        for (int i = 0; i < Budget; i++)
            Send(a, "x");
        a.Clear();

        Send(a, "/invite Kaela"); // runs although the sender is over the limit
        Assert.DoesNotContain(a.Lines(), l => l.Contains("too fast", StringComparison.Ordinal));
        Assert.Single(b.Invites());

        // They take no budget: a fresh sender runs them freely, then still has all of it.
        PartyClient c = _w.Online(3, "Tom");
        for (int i = 0; i < 2 * Budget; i++)
        {
            Send(c, "/invite Nobody");
            Send(c, "/leave");
        }

        c.Clear();
        for (int i = 0; i < Budget; i++)
            Send(c, "hello");
        Assert.Empty(c.Lines());
        Assert.Equal(Budget, Heard(c, ChatChannel.Say).Count);
    }

    [Fact]
    public void Never_reach_a_recipient_over_the_limit()
    {
        PartyClient a = _w.Online(1, "Aren");
        PartyClient b = _w.Online(2, "Kaela");
        _w.Form(a, b);
        for (int i = 0; i < Budget; i++)
            Send(a, "/w Kaela spend");
        b.Clear();

        Send(a, "/w Kaela over");
        Send(a, "/p over");

        Assert.Empty(b.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE));
    }

    [Fact]
    public void Not_say_an_over_limit_plain_message_to_the_instance()
    {
        IWorld world = MapInstanceClients.NewWorld();
        MapInstance here = TestMapInstances.Build(world);
        world.InstanceRegistry.GetInstanceById(here.InstanceId).Returns(here);
        var handler = new ChatMessageHandler(world, Substitute.For<ICommandDispatcher>(), _limiter);
        MapInstanceClient sender = MapInstanceClients.Join(here, 1);
        MapInstanceClient neighbour = MapInstanceClients.Join(here, 2);

        for (int i = 0; i < Budget + 2; i++)
            handler.Execute(sender.Connection, new CChatMessagePacket { Message = $"m{i}", DateTime = DateTime.UtcNow });

        Assert.Equal(Budget, neighbour.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Turn_the_limit_off_at_zero_or_below(int setting)
    {
        _config.ChatMessagesPerMinute = setting;
        PartyClient a = _w.Online(1, "Aren");

        for (int i = 0; i < 50; i++)
            Send(a, "spam");

        Assert.Empty(a.Lines());
        Assert.Equal(50, Heard(a, ChatChannel.Say).Count);
        Assert.Equal(0, _limiter.TrackedCharacters);
    }

    [Fact]
    public void Forget_a_character_and_hold_nothing_for_a_sender_with_no_messages()
    {
        PartyClient a = _w.Online(1, "Aren");
        Assert.True(_limiter.Check(1, out _));
        Assert.Equal(0, _limiter.TrackedCharacters);

        for (int i = 0; i < Budget; i++)
            Send(a, "x");
        Assert.Equal(1, _limiter.TrackedCharacters);
        Assert.False(_limiter.Check(1, out _));

        _limiter.Forget(1);

        Assert.Equal(0, _limiter.TrackedCharacters);
        Assert.True(_limiter.Check(1, out _));
    }

    [Fact]
    public void Drop_a_window_that_has_aged_out_when_next_asked()
    {
        _limiter.Record(1);
        _time.Advance(ChatRateLimiter.Window);

        Assert.True(_limiter.Check(1, out _));
        Assert.Equal(0, _limiter.TrackedCharacters);
    }

    [Fact]
    public async Task Forget_a_character_when_it_leaves_the_world()
    {
        TestStaticDataRepositories r = TestStaticData.Repositories();
        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration { WorldId = new Avalon.Domain.Auth.WorldId(1) }),
            Substitute.For<IServiceProvider>(),
            Substitute.For<IWorldRepository>(),
            Substitute.For<IAvalonMapManager>(),
            Substitute.For<IServiceScopeFactory>(),
            r.CreateInfos, r.ClassStats, r.Items, r.Abilities, r.Levels, r.Creatures, r.BaseStats, r.Rarities,
            r.Texts,
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            r.Dialogue, r.Loot, _limiter);
        PartyClient a = _w.Online(1, "Aren");
        Send(a, "x");
        Assert.Equal(1, _limiter.TrackedCharacters);

        await world.LeaveWorldAsync(a.Connection);

        Assert.Equal(0, _limiter.TrackedCharacters);
    }
}

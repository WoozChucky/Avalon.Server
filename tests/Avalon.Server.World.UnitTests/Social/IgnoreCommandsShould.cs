using Avalon.Common.ValueObjects;
using Avalon.Database.Character.Repositories;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Social;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Social;

/// <summary>
/// /ignore, /unignore and /ignorelist (#723): synchronous on the tick, an offline name looked up through ctx.Then, every
/// answer a system line, and the whole list sent after every change.
/// </summary>
public class IgnoreCommandsShould
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly PartyTestWorld _w = new();
    private readonly ICharacterIgnoreRepository _repository = Substitute.For<ICharacterIgnoreRepository>();
    private readonly GameConfiguration _config = new();

    public IgnoreCommandsShould()
    {
        _repository.FindCharacterByNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CharacterNameMatch?>(null));
    }

    private DateTime ClockNow => _w.Clock.GetUtcNow().UtcDateTime;

    private PartyClient Online(uint id, string name)
    {
        PartyClient client = _w.Online(id, name);
        // What the tick does a tick later, done now: the task settles, then the callback runs.
        client.Connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci =>
            {
                ci.Arg<Task>().Wait(TimeSpan.FromSeconds(5));
                ci.Arg<Action>()();
            });
        return client;
    }

    private IgnoreCommand Ignore(ChatRateLimiter? limiter = null) =>
        new(_w.Parties.Online, _repository, Options.Create(_config), _w.Clock, limiter ?? Chat.ChatLimits.Off());
    private UnignoreCommand Unignore() => new();
    private IgnoreListCommand List() => new(Options.Create(_config));

    private static void Run(ICommand command, PartyClient client, string message)
    {
        string[] parts = message.TrimStart('/').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        command.Execute(new CommandContext(client.Connection, new CChatMessagePacket { Message = message, DateTime = Now },
            e => throw e), parts[1..]);
    }

    private static List<SIgnoreListPacket> Lists(PartyClient client) =>
        client.Read<SIgnoreListPacket>(NetworkPacketType.SMSG_IGNORE_LIST);

    [Fact]
    public void Ignore_an_online_character_by_name_without_a_lookup()
    {
        PartyClient aren = Online(1, "Aren");
        Online(2, "Kaela");

        Run(Ignore(), aren, "/ignore kAELA");

        Assert.Equal([new IgnoredCharacter(2, "Kaela", ClockNow)], aren.Character.Ignores.Entries);
        Assert.Equal(["You are now ignoring Kaela."], aren.Lines());
        IgnoredCharacterDto sent = Assert.Single(Assert.Single(Lists(aren)).Characters);
        Assert.Equal((2u, "Kaela"), (sent.CharacterId, sent.Name));
        Assert.True(aren.Character.SaveState.TakeMarks().Ignores!.ContainsKey(2));
        _repository.DidNotReceiveWithAnyArgs().FindCharacterByNameAsync(default!, default);
    }

    [Fact]
    public void Ignore_an_offline_character_found_in_the_database()
    {
        PartyClient aren = Online(1, "Aren");
        _repository.FindCharacterByNameAsync("borin", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CharacterNameMatch?>(new CharacterNameMatch(9u, "Borin")));

        Run(Ignore(), aren, "/ignore borin");

        Assert.Equal([new IgnoredCharacter(9, "Borin", ClockNow)], aren.Character.Ignores.Entries);
        Assert.Equal(["You are now ignoring Borin."], aren.Lines());
        Assert.Equal("Borin", Assert.Single(Assert.Single(Lists(aren)).Characters).Name);
    }

    [Fact]
    public void Refuse_a_name_no_character_has()
    {
        PartyClient aren = Online(1, "Aren");

        Run(Ignore(), aren, "/ignore Nobody");

        Assert.Equal(["No character named Nobody exists."], aren.Lines());
        Assert.Empty(aren.Character.Ignores.Entries);
        Assert.Empty(Lists(aren));
    }

    [Theory]
    [InlineData("/ignore")]
    [InlineData("/ignore two words")]
    public void Answer_usage_without_exactly_one_name(string message)
    {
        PartyClient aren = Online(1, "Aren");

        Run(Ignore(), aren, message);

        Assert.Equal(["Usage: /ignore <name>"], aren.Lines());
    }

    [Fact]
    public void Refuse_to_ignore_yourself()
    {
        PartyClient aren = Online(1, "Aren");

        Run(Ignore(), aren, "/ignore aren");

        Assert.Equal(["You can't ignore yourself."], aren.Lines());
        Assert.Empty(aren.Character.Ignores.Entries);
        Assert.Empty(Lists(aren));
        _repository.DidNotReceiveWithAnyArgs().FindCharacterByNameAsync(default!, default);
    }

    [Fact]
    public void Refuse_to_ignore_yourself_found_by_id()
    {
        PartyClient aren = Online(1, "Aren");
        _repository.FindCharacterByNameAsync("Arén", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CharacterNameMatch?>(new CharacterNameMatch(1u, "Aren")));

        Run(Ignore(), aren, "/ignore Arén");

        Assert.Equal(["You can't ignore yourself."], aren.Lines());
        Assert.Empty(aren.Character.Ignores.Entries);
    }

    [Fact]
    public void Refuse_a_character_already_on_the_list()
    {
        PartyClient aren = Online(1, "Aren");
        Online(2, "Kaela");
        aren.Character.Ignores.Add(2, "Kaela", Now);

        Run(Ignore(), aren, "/ignore KAELA");

        Assert.Equal(["Kaela is already on your ignore list."], aren.Lines());
        Assert.Single(aren.Character.Ignores.Entries);
        Assert.Empty(Lists(aren));
    }

    [Fact]
    public void Refuse_a_character_already_on_the_list_under_another_name()
    {
        PartyClient aren = Online(1, "Aren");
        aren.Character.Ignores.Add(9, "Borin", Now);
        _repository.FindCharacterByNameAsync("Borrin", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CharacterNameMatch?>(new CharacterNameMatch(9u, "Borrin")));

        Run(Ignore(), aren, "/ignore Borrin");

        Assert.Equal(["Borin is already on your ignore list."], aren.Lines());
        Assert.Single(aren.Character.Ignores.Entries);
    }

    [Fact]
    public void Refuse_once_the_list_is_full()
    {
        _config.MaxIgnoredCharacters = 1;
        PartyClient aren = Online(1, "Aren");
        Online(2, "Kaela");
        Online(3, "Tom");
        Run(Ignore(), aren, "/ignore Kaela");
        aren.Clear();

        Run(Ignore(), aren, "/ignore Tom");

        Assert.Equal(["Your ignore list is full (1/1)."], aren.Lines());
        Assert.False(aren.Character.Ignores.Contains(3));
        Assert.Empty(Lists(aren));
    }

    [Fact]
    public void Refuse_an_offline_character_once_the_list_filled_during_the_lookup()
    {
        _config.MaxIgnoredCharacters = 1;
        PartyClient aren = _w.Online(1, "Aren");
        Action? callback = null;
        aren.Connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci => callback = ci.Arg<Action>());
        _repository.FindCharacterByNameAsync("Borin", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CharacterNameMatch?>(new CharacterNameMatch(9u, "Borin")));

        Run(Ignore(), aren, "/ignore Borin");
        aren.Character.Ignores.Add(2, "Kaela", Now);
        callback!();

        Assert.Equal(["Your ignore list is full (1/1)."], aren.Lines());
        Assert.False(aren.Character.Ignores.Contains(9));
    }

    [Fact]
    public void Drop_a_lookup_that_completes_after_the_character_left()
    {
        PartyClient aren = _w.Online(1, "Aren");
        Action? callback = null;
        aren.Connection.When(c => c.EnqueueContinuation(Arg.Any<Task>(), Arg.Any<Action>()))
            .Do(ci => callback = ci.Arg<Action>());
        _repository.FindCharacterByNameAsync("Borin", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CharacterNameMatch?>(new CharacterNameMatch(9u, "Borin")));

        Run(Ignore(), aren, "/ignore Borin");
        aren.Connection.Character.Returns((ICharacter?)null);
        callback!();

        Assert.Empty(aren.Sent);
        Assert.Empty(aren.Character.Ignores.Entries);
    }

    [Fact]
    public void Refuse_over_the_chat_rate_limit_before_any_lookup()
    {
        _config.ChatMessagesPerMinute = 1;
        var limiter = new ChatRateLimiter(Options.Create(_config), _w.Clock);
        PartyClient aren = Online(1, "Aren");
        Online(2, "Kaela");
        limiter.Record(aren.Id);

        Run(Ignore(limiter), aren, "/ignore Borin");

        Assert.Equal([ChatRateLimiter.TooFast(TimeSpan.FromSeconds(60))], aren.Lines());
        Assert.Empty(aren.Character.Ignores.Entries);
        _repository.DidNotReceiveWithAnyArgs().FindCharacterByNameAsync(default!, default);
    }

    [Fact]
    public void Spend_the_chat_rate_limit_like_a_chat_message()
    {
        _config.ChatMessagesPerMinute = 1;
        var limiter = new ChatRateLimiter(Options.Create(_config), _w.Clock);
        PartyClient aren = Online(1, "Aren");
        PartyClient kaela = Online(2, "Kaela");

        Run(Ignore(limiter), aren, "/ignore Nobody");   // a lookup that finds nobody still spends it
        aren.Clear();
        Run(new WhisperCommand(_w.Parties.Online, limiter), aren, "/w Kaela hi");

        Assert.Equal([ChatRateLimiter.TooFast(TimeSpan.FromSeconds(60))], aren.Lines());
        Assert.Empty(kaela.Sent);
    }

    [Fact]
    public void Spend_nothing_on_a_refusal_made_in_memory()
    {
        _config.ChatMessagesPerMinute = 1;
        var limiter = new ChatRateLimiter(Options.Create(_config), _w.Clock);
        PartyClient aren = Online(1, "Aren");
        Online(2, "Kaela");

        Run(Ignore(limiter), aren, "/ignore Aren");
        Run(Ignore(limiter), aren, "/ignore Kaela");

        Assert.True(aren.Character.Ignores.Contains(2));
    }

    [Fact]
    public void Unignore_a_character_on_the_list_by_name()
    {
        PartyClient aren = Online(1, "Aren");
        aren.Character.Ignores.Add(2, "Kaela", Now);
        aren.Character.Ignores.Add(9, "Borin", Now);
        aren.Character.SaveState.Acknowledge(aren.Character.SaveState.TakeMarks());

        Run(Unignore(), aren, "/unignore kaela");

        Assert.Equal([9u], aren.Character.Ignores.Entries.Select(e => e.Id));
        Assert.Equal(["You are no longer ignoring Kaela."], aren.Lines());
        Assert.Equal([9u], Assert.Single(Lists(aren)).Characters.Select(c => c.CharacterId));
        Assert.True(aren.Character.SaveState.TakeMarks().Ignores!.ContainsKey(2));
    }

    [Fact]
    public void Refuse_to_unignore_a_name_not_on_the_list()
    {
        PartyClient aren = Online(1, "Aren");

        Run(Unignore(), aren, "/unignore Kaela");

        Assert.Equal(["Kaela is not on your ignore list."], aren.Lines());
        Assert.Empty(Lists(aren));
    }

    [Fact]
    public void Answer_unignore_usage_without_exactly_one_name()
    {
        PartyClient aren = Online(1, "Aren");

        Run(Unignore(), aren, "/unignore");

        Assert.Equal(["Usage: /unignore <name>"], aren.Lines());
    }

    [Fact]
    public void Say_so_when_the_list_is_empty()
    {
        PartyClient aren = Online(1, "Aren");

        Run(List(), aren, "/ignorelist");

        Assert.Equal(["You are not ignoring anyone."], aren.Lines());
    }

    [Fact]
    public void List_the_ignored_characters_oldest_first()
    {
        PartyClient aren = Online(1, "Aren");
        aren.Character.Ignores.Add(9, "Borin", Now);
        aren.Character.Ignores.Add(2, "Kaela", Now);

        Run(List(), aren, "/ignorelist");

        Assert.Equal(["Ignoring 2/50: Borin, Kaela."], aren.Lines());
    }

    [Fact]
    public void Hear_an_unignored_character_again()
    {
        PartyClient aren = Online(1, "Aren");
        PartyClient kaela = Online(2, "Kaela");
        Run(Ignore(), kaela, "/ignore Aren");
        Run(Unignore(), kaela, "/unignore Aren");
        kaela.Clear();

        Run(new WhisperCommand(_w.Parties.Online, Chat.ChatLimits.Off()), aren, "/w Kaela back");

        Assert.Equal("back", Assert.Single(kaela.Read<SChatMessagePacket>(NetworkPacketType.SMSG_CHAT_MESSAGE)).Message);
    }
}

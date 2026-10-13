using Avalon.Common.Accounts;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Parties;
using Avalon.World.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// The requests a client can send as fast as its connection is read, a party request, a chat message over its limit, a
/// command that is not one, are answered without allocating (#883): no scenario sends any of them, so the scenario
/// gate would not see a closure or a string come back.
/// </summary>
/// <remarks>
/// Every path here sends its answer, so these run where <see cref="InventoryAllocationShould" /> does and read the
/// fewest of three windows, for the reason given there.
/// </remarks>
[Collection(nameof(ScenarioAllocations))]
public class GameplayRequestAllocationShould
{
    private const int Rounds = 100;

    private static readonly Action<Exception> s_ignore = static _ => { };

    private readonly CharacterEntity _character = New();
    private readonly QuietConnection _connection;

    public GameplayRequestAllocationShould() => _connection = new QuietConnection(_character);

    [Fact]
    public void Answer_a_party_request_allocating_nothing()
    {
        var parties = new PartyService(Options.Create(new GameConfiguration()), TimeProvider.System,
            NullLogger<PartyService>.Instance);
        var invite = new PartyInviteHandler(parties, NullLogger<PartyInviteHandler>.Instance);
        var respond = new PartyInviteResponseHandler(parties, NullLogger<PartyInviteResponseHandler>.Instance);
        var kick = new PartyKickHandler(parties, NullLogger<PartyKickHandler>.Instance);
        var promote = new PartyPromoteHandler(parties, NullLogger<PartyPromoteHandler>.Instance);
        var leave = new PartyLeaveHandler(parties, NullLogger<PartyLeaveHandler>.Instance);
        var mode = new PartyExperienceModeHandler(parties, NullLogger<PartyExperienceModeHandler>.Instance);

        // Each is refused (a name that is none, no invite, no party), which is what a client can repeat for ever.
        var nobody = new CPartyInvitePacket { TargetName = "no such name" };
        var yes = new CPartyInviteResponsePacket { Accept = true };
        var out9 = new CPartyKickPacket { CharacterId = 9 };
        var up9 = new CPartyPromotePacket { CharacterId = 9 };
        var gone = new CPartyLeavePacket();
        var even = new CPartyExperienceModePacket { Mode = PartyExperienceMode.Even };

        long bytes = ThreadAllocations.FewestOfThreeWindows(Rounds, () =>
        {
            invite.Execute(_connection, nobody);
            respond.Execute(_connection, yes);
            kick.Execute(_connection, out9);
            promote.Execute(_connection, up9);
            leave.Execute(_connection, gone);
            mode.Execute(_connection, even);
        });

        Assert.Equal(0, bytes);
        Assert.Equal(6 * 4 * Rounds, _connection.Sent);
    }

    [Fact]
    public void Refuse_a_chat_message_and_run_a_command_allocating_only_what_the_command_is_handed()
    {
        var limiter = new ChatRateLimiter(Options.Create(new GameConfiguration { ChatMessagesPerMinute = 1 }),
            new FakeTimeProvider());
        limiter.Record(_character.Guid.Id);
        var counted = new Counted();
        var handler = new ChatMessageHandler(new QuietWorld(null!),
            new CommandDispatcher([counted, new StaffOnly()], NullLogger<CommandDispatcher>.Instance), limiter);

        DateTime now = DateTime.UtcNow;
        var overTheLimit = new CChatMessagePacket { Message = "one more thing", DateTime = now };
        var unknown = new CChatMessagePacket { Message = "/dance with me", DateTime = now };
        var forbidden = new CChatMessagePacket { Message = "/staff only", DateTime = now };
        var known = new CChatMessagePacket { Message = "/count this  much", DateTime = now };

        long tooFast = ThreadAllocations.FewestOfThreeWindows(Rounds, () => handler.Execute(_connection, overTheLimit));
        long noSuchCommand = ThreadAllocations.FewestOfThreeWindows(Rounds, () => handler.Execute(_connection, unknown));
        long notAllowed = ThreadAllocations.FewestOfThreeWindows(Rounds, () => handler.Execute(_connection, forbidden));
        int answered = _connection.Sent;
        long ran = ThreadAllocations.FewestOfThreeWindows(Rounds, () => handler.Execute(_connection, known));

        // A command is handed a context and its arguments, which are its own to keep: built here to be measured.
        long handed = ThreadAllocations.FewestOfThreeWindows(Rounds, () =>
        {
            GC.KeepAlive(new CommandContext(_connection, known, s_ignore));
            GC.KeepAlive(new[] { new string('t', 4), new string('m', 4) });
        });

        Assert.Multiple(
            () => Assert.Equal(0, tooFast),
            () => Assert.Equal(0, noSuchCommand),
            () => Assert.Equal(0, notAllowed),
            () => Assert.Equal(handed, ran));
        Assert.Equal(3 * 4 * Rounds, answered);
        Assert.Equal(4 * Rounds * 2, counted.Arguments);
    }

    private sealed class Counted : ICommand
    {
        public int Arguments { get; private set; }
        public string Name => "count";
        public string[] Aliases => [];
        public void Execute(CommandContext ctx, string[] args) => Arguments += args.Length;
    }

    private sealed class StaffOnly : ICommand
    {
        public string Name => "staff";
        public string[] Aliases => [];
        public AccountAccessLevel RequiredAccess => AccessLevels.GameMaster;
        public void Execute(CommandContext ctx, string[] args) => throw new InvalidOperationException("Not for a player.");
    }
}

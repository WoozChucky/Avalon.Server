using Avalon.Common.Accounts;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Avalon.World.Handlers;
using Avalon.World.Parties;
using Avalon.World.Social;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// A town's social life, as its players' clients would send it: parties of three that keep changing, and chat. Every
/// event is a client packet handed to the real handler (<see cref="PartyLeaveHandler" />,
/// <see cref="PartyInviteHandler" />, <see cref="PartyInviteResponseHandler" />, <see cref="ChatMessageHandler" />, the
/// last over the real <see cref="CommandDispatcher" />, <see cref="ChatRateLimiter" /> and commands), from
/// <see cref="ScenarioConnection.UpdateMap" />, inside the instance pass, where production handles a connection's
/// queued map packets: inside the tick's input dispatch, as the forest's casts are. One packet of each kind, prepared
/// before the first tick, so the driver itself allocates nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The schedule</b> repeats every <see cref="PeriodTicks" /> ticks, one window's worth, and every period holds one
/// event of each kind at a fixed tick (<see cref="LeaveAt" /> and the others): any 60 ticks in a row, wherever the
/// measured windows start, hold the same seven events, made by players in the same state. So every window measures
/// them all, and the least window, the gated figure, is any window. Which player acts is a function of the period
/// alone, read from the world's clock, so the same build runs the same events on every run.
/// </para>
/// <para>
/// <b>Parties.</b> Player 3k, 3k+1 and 3k+2 form party k at the start (setup, through the service). Period w is party
/// w mod 10's turn: its leader leaves through the leave packet (the leadership goes to the longest-standing member
/// left, the party keeps two), the new leader invites the one who left by name, and it accepts, joining last. The
/// longest-standing member is then the next to lead and the next to leave, so the three take turns: each player
/// leaves, is invited and accepts once every 30 periods, 30 s, and a party never falls below two (it is never
/// disbanded, so none is formed again either).
/// </para>
/// <para>
/// <b>Chat.</b> In every period one player says a line to the town, one whispers another player in another party
/// (<c>/w</c>), one speaks to its party (<c>/p</c>, after the accept, so every party is whole) and one runs
/// <c>/ignorelist</c>, the cheap command: no database, no state, one constant line back while the list is empty, as
/// every list here is. Player p says its line in period w = p (mod 30), whispers in w = p + 20 and speaks to its party
/// in w = p + 10: a line every 10 s, six a minute, inside the rate limit's 10 a minute (<c>Game:ChatMessagesPerMinute</c>
/// as production sets it), so none is refused; its <c>/ignorelist</c>, which the limit does not count, comes once every
/// 30 s.
/// </para>
/// <para>
/// <b>Checks.</b> Every event is checked as it is handed in, by what it changed: the party left with its new leader,
/// the invite sent to the one invited, the party whole again, a said line sent to every player in the town, a whisper
/// to its target and its echo to the sender, a party line to each member, and the command run and answered. The counts
/// are what <see cref="TownSocialScenario.Verify" /> reads; a failed check is counted, never thrown inside a tick.
/// Reading the counters allocates nothing.
/// </para>
/// </remarks>
public sealed class SocialSchedule
{
    /// <summary>The schedule's period: one measured window.</summary>
    public const int PeriodTicks = ScenarioMeasurement.WindowTicks;

    /// <summary>Players per party.</summary>
    public const int PartySize = 3;

    // Each period's events, by tick within the period.
    public const int LeaveAt = 0;
    public const int SayAt = 10;
    public const int InviteAt = 20;
    public const int WhisperAt = 30;
    public const int AcceptAt = 40;
    public const int CommandAt = 45;
    public const int PartyChatAt = 50;

    // Which player acts in period w: the sayer is w mod players, the others this many periods on.
    private const int WhisperShift = 10;
    private const int PartyChatShift = 20;
    private const int CommandShift = 5;

    // Whom a player whispers: this many players on, so always another party.
    private const int WhisperTargetShift = 15;

    private const string SayLine = "Anyone heading out to the forest?";
    private const string PartyLine = "/p Ready when you are, see you at the gate.";
    private const string WhisperText = "Meet me by the town gate.";
    private const string CommandLine = "/ignorelist";

    // The client's own clock on a chat message, which the server never reads (it stamps every line with its own, #944):
    // fixed, so every run sends the same.
    private static readonly DateTime s_clientTime = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private readonly ScenarioWorld _world;
    private readonly PartyService _parties;
    private readonly DateTimeOffset _start;
    private readonly int _players;
    private readonly int _partyCount;
    private readonly ScenarioConnection?[] _connections;
    private readonly string[] _names;
    private readonly int[] _sentBefore;

    private readonly PartyLeaveHandler _leave;
    private readonly PartyInviteHandler _invite;
    private readonly PartyInviteResponseHandler _respond;
    private readonly ChatMessageHandler _chat;
    private readonly CountingCommand _ignoreList;

    private readonly CPartyLeavePacket _leavePacket = new();
    private readonly CPartyInvitePacket _invitePacket = new();
    private readonly CPartyInviteResponsePacket _acceptPacket = new() { Accept = true };
    private readonly CChatMessagePacket _sayPacket = new() { Message = SayLine, DateTime = s_clientTime };
    private readonly CChatMessagePacket _partyPacket = new() { Message = PartyLine, DateTime = s_clientTime };
    private readonly CChatMessagePacket _commandPacket = new() { Message = CommandLine, DateTime = s_clientTime };
    private readonly CChatMessagePacket[] _whisperPackets;

    // What was counted when the world's progress was marked.
    private SocialCounts _marked;

    /// <param name="world">A world with reference data: it has the party service.</param>
    /// <param name="players">The players, a whole number of parties; each joins through <see cref="Add" />.</param>
    public SocialSchedule(ScenarioWorld world, int players)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (players <= 0 || players % PartySize != 0)
            throw new ArgumentException($"The players must be whole parties of {PartySize}", nameof(players));

        _world = world;
        _parties = world.Parties ?? throw new ArgumentException(
            "The town's parties need reference data: build the world with ScenarioWorld.CreateWithReferenceData", nameof(world));
        _start = world.Clock.GetUtcNow();
        _players = players;
        _partyCount = players / PartySize;
        _connections = new ScenarioConnection?[players];
        _names = new string[players];
        _sentBefore = new int[players];
        _whisperPackets = new CChatMessagePacket[players];
        for (int p = 0; p < players; p++)
            _names[p] = NameOf(p);
        for (int p = 0; p < players; p++)
        {
            _whisperPackets[p] = new CChatMessagePacket
            {
                Message = $"/w {_names[WhisperTarget(p)]} {WhisperText}",
                DateTime = s_clientTime,
            };
        }

        // As production registers them: one rate limiter for plain chat and the commands that count, the commands the
        // dispatcher resolves (only those the scenario sends: the lookup is a dictionary whatever it holds), and every
        // handler's and the dispatcher's log on the world's, so an error they contain ends the scenario.
        IOptions<Configuration.GameConfiguration> options = Options.Create(world.Host.Configuration);
        var limiter = new ChatRateLimiter(options, world.Clock);
        _ignoreList = new CountingCommand(new IgnoreListCommand(options));
        var dispatcher = new CommandDispatcher(
            [new PartyChatCommand(_parties, limiter), new WhisperCommand(_parties.Online, limiter), _ignoreList],
            new Logger<CommandDispatcher>(world.Logs), world.Clock);

        _leave = new PartyLeaveHandler(_parties, new Logger<PartyLeaveHandler>(world.Logs));
        _invite = new PartyInviteHandler(_parties, new Logger<PartyInviteHandler>(world.Logs));
        _respond = new PartyInviteResponseHandler(_parties, new Logger<PartyInviteResponseHandler>(world.Logs));
        _chat = new ChatMessageHandler(world.Host, dispatcher, limiter, world.Clock);
    }

    /// <summary>
    /// Player <paramref name="player" />'s character name: "Social" and two letters, so every name is a valid
    /// character name (letters only, which the invite and the whisper check) of the same length, and every line that
    /// carries one is the same size.
    /// </summary>
    public static string NameOf(int player)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, 26 * 26);
        return CharacterName.Display($"Social{(char)('a' + player / 26)}{(char)('a' + player % 26)}");
    }

    /// <summary>What the schedule's events did since the world was built.</summary>
    public SocialCounts Counts { get; private set; }

    /// <summary>What they did since the world's progress was marked (<see cref="Mark" />).</summary>
    public SocialCounts SinceMark => Counts.Since(_marked);

    /// <summary>The first check that failed, for the scenario's error; null while none has.</summary>
    public string? FirstFailure { get; private set; }

    /// <summary>Records the counts, for <see cref="SinceMark" />: the world raises it before its measured windows.</summary>
    public void Mark() => _marked = Counts;

    /// <summary>Registers player <paramref name="player" />'s connection, once it is connected.</summary>
    public void Add(int player, ScenarioConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Character is not { } character || character.Name != _names[player] || character.Guid.Id != player + 1)
        {
            throw new ArgumentException(
                $"Player {player} must be character {player + 1}, named {_names[player]}: a party's members are found by id",
                nameof(connection));
        }

        _connections[player] = connection;
    }

    /// <summary>
    /// Player <paramref name="player" />'s packets this tick, if the schedule gives it one: a connection's map
    /// update.
    /// </summary>
    public void Step(int player, ScenarioConnection connection)
    {
        long tick = (_world.Clock.GetUtcNow() - _start).Ticks / ScenarioWorld.Dt.Ticks;
        int period = (int)(tick / PeriodTicks);
        int offset = (int)(tick % PeriodTicks);

        switch (offset)
        {
            case LeaveAt when player == Leaver(period):
                Leave(player, connection, NewLeader(period));
                break;
            case SayAt when player == period % _players:
                Say(connection);
                break;
            case InviteAt when player == NewLeader(period):
                Invite(connection, Leaver(period));
                break;
            case WhisperAt when player == (period + WhisperShift) % _players:
                Whisper(player, connection);
                break;
            case AcceptAt when player == Leaver(period):
                Accept(player, connection, NewLeader(period));
                break;
            case CommandAt when player == (period + CommandShift) % _players:
                Command(player, connection);
                break;
            case PartyChatAt when player == (period + PartyChatShift) % _players:
                PartyChat(player, connection);
                break;
        }
    }

    // Period w is party (w mod parties)'s turn, its (w / parties)th: member j of party k is player 3k + j, and the
    // turns go round them in join order, each leaving as the leader and handing over to the next.
    private int Leaver(int period) => Member(period, 0);

    private int NewLeader(int period) => Member(period, 1);

    private int Member(int period, int ahead)
    {
        int party = period % _partyCount;
        int turn = period / _partyCount;
        return party * PartySize + (turn + ahead) % PartySize;
    }

    private int WhisperTarget(int player) => (player + WhisperTargetShift) % _players;

    private static uint IdOf(ScenarioConnection connection) => connection.Character!.Guid.Id;

    private void Leave(int player, ScenarioConnection connection, int newLeader)
    {
        uint id = IdOf(connection);
        Party? party = _parties.PartyOf(id);
        if (party is null || !party.IsLeader(id))
        {
            Fail(player, "was to leave as its party's leader, and is not");
            return;
        }

        _leave.Execute(connection, _leavePacket);

        if (_parties.PartyOf(id) is null && party.Members.Count == PartySize - 1
                                         && party.IsLeader(IdOf(_connections[newLeader]!)))
        {
            Counts = Counts with { Left = Counts.Left + 1 };
        }
        else
        {
            Fail(player, "left, but the party did not pass to the next leader with the others still in it");
        }
    }

    private void Invite(ScenarioConnection connection, int invited)
    {
        ScenarioConnection target = _connections[invited]!;
        int before = target.Sent;

        _invitePacket.TargetName = _names[invited];
        _invite.Execute(connection, _invitePacket);

        // The one invited is sent the invite; the leader its answer.
        if (target.Sent == before + 1)
            Counts = Counts with { Invited = Counts.Invited + 1 };
        else
            Fail(invited, "was not sent its party invite");
    }

    private void Accept(int player, ScenarioConnection connection, int leader)
    {
        Party? party = _parties.PartyOf(IdOf(_connections[leader]!));

        _respond.Execute(connection, _acceptPacket);

        if (party is not null && ReferenceEquals(_parties.PartyOf(IdOf(connection)), party)
                              && party.Members.Count == PartySize)
        {
            Counts = Counts with { Accepted = Counts.Accepted + 1 };
        }
        else
        {
            Fail(player, "accepted its invite, and is not back in a whole party");
        }
    }

    private void Say(ScenarioConnection connection)
    {
        for (int p = 0; p < _players; p++)
            _sentBefore[p] = _connections[p]!.Sent;

        _chat.Execute(connection, _sayPacket);

        // Everyone in the town hears it, the sender included.
        for (int p = 0; p < _players; p++)
        {
            if (_connections[p]!.Sent != _sentBefore[p] + 1)
            {
                Fail(p, "did not hear a line said in the town");
                return;
            }
        }

        Counts = Counts with { Said = Counts.Said + 1 };
    }

    private void Whisper(int player, ScenarioConnection connection)
    {
        ScenarioConnection target = _connections[WhisperTarget(player)]!;
        int targetBefore = target.Sent;
        int senderBefore = connection.Sent;

        _chat.Execute(connection, _whisperPackets[player]);

        // The line to its target, and the echo to the sender.
        if (target.Sent == targetBefore + 1 && connection.Sent == senderBefore + 1)
            Counts = Counts with { Whispered = Counts.Whispered + 1 };
        else
            Fail(player, "whispered, and the line did not reach its target with its echo back");
    }

    private void PartyChat(int player, ScenarioConnection connection)
    {
        Party? party = _parties.PartyOf(IdOf(connection));
        if (party is null || party.Members.Count != PartySize)
        {
            Fail(player, "was to speak to a whole party, and is not in one");
            return;
        }

        for (int m = 0; m < PartySize; m++)
            _sentBefore[m] = _connections[(int)party.Members[m].Id.Value - 1]!.Sent;

        _chat.Execute(connection, _partyPacket);

        // Every member hears it, the sender included.
        for (int m = 0; m < PartySize; m++)
        {
            if (_connections[(int)party.Members[m].Id.Value - 1]!.Sent != _sentBefore[m] + 1)
            {
                Fail(player, "spoke to its party, and a member did not hear it");
                return;
            }
        }

        Counts = Counts with { PartyLines = Counts.PartyLines + 1 };
    }

    private void Command(int player, ScenarioConnection connection)
    {
        int runs = _ignoreList.Runs;
        int before = connection.Sent;

        _chat.Execute(connection, _commandPacket);

        // The command ran, and its line came back: "Unknown command." would be a line too, but not a run.
        if (_ignoreList.Runs == runs + 1 && connection.Sent == before + 1)
            Counts = Counts with { Commands = Counts.Commands + 1 };
        else
            Fail(player, "ran /ignorelist, and it did not run and answer");
    }

    /// <summary>A failed check: counted, the first kept for the error. Allocates, but a failed run fails anyway.</summary>
    private void Fail(int player, string what)
    {
        Counts = Counts with { Failures = Counts.Failures + 1 };
        FirstFailure ??= $"player {player} ({_names[player]}) {what}, at tick " +
                         $"{(_world.Clock.GetUtcNow() - _start).Ticks / ScenarioWorld.Dt.Ticks}";
    }

    /// <summary>
    /// The command the scenario runs, counted as the dispatcher runs it: the proof that it was found and run, which the
    /// line it answers with cannot give (an unknown command is answered with a line too).
    /// </summary>
    private sealed class CountingCommand(ICommand inner) : ICommand
    {
        public int Runs { get; private set; }

        public string Name => inner.Name;

        public string[] Aliases => inner.Aliases;

        public AccountAccessLevel RequiredAccess => inner.RequiredAccess;

        public void Execute(CommandContext ctx, string[] args)
        {
            Runs++;
            inner.Execute(ctx, args);
        }
    }
}

/// <summary>What a <see cref="SocialSchedule" />'s events did, each counted once its check passed.</summary>
/// <param name="Left">Leaders that left, the party passing to the next.</param>
/// <param name="Invited">Invites sent to the one who left.</param>
/// <param name="Accepted">Invites accepted, the party whole again.</param>
/// <param name="Said">Lines said to the whole town.</param>
/// <param name="Whispered">Whispers delivered to their target, echoed to the sender.</param>
/// <param name="PartyLines">Party lines delivered to every member.</param>
/// <param name="Commands"><c>/ignorelist</c> runs answered.</param>
/// <param name="Failures">Checks that failed.</param>
public readonly record struct SocialCounts(int Left, int Invited, int Accepted, int Said, int Whispered,
    int PartyLines, int Commands, int Failures)
{
    /// <summary>What happened between <paramref name="earlier" /> and these counts.</summary>
    public SocialCounts Since(SocialCounts earlier) =>
        new(Left - earlier.Left, Invited - earlier.Invited, Accepted - earlier.Accepted, Said - earlier.Said,
            Whispered - earlier.Whispered, PartyLines - earlier.PartyLines, Commands - earlier.Commands,
            Failures - earlier.Failures);
}

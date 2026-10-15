using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Social;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Parties;
using Avalon.World.Public.Enums;
using Avalon.World.Testing.Scenarios;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Avalon.Benchmarking.Benchmarks;

/// <summary>
/// One of each of a town's social events (#973), as the <c>town-social</c> scenario makes them: 30 players in a town, in
/// ten parties of three, the client packets handed to the real handlers (<see cref="ChatMessageHandler" /> over the real
/// <see cref="CommandDispatcher" />, <see cref="PartyChatCommand" /> and <see cref="WhisperCommand" />; the party
/// handlers over the real <see cref="PartyService" />), and what they send framed and sealed by the send pass, as a tick
/// ends. <see cref="SaidLine" />: a line said to the town, heard by all 30. <see cref="Whisper" />: <c>/w</c> of seven
/// words to a player in another party, and its echo. <see cref="PartyLine" />: <c>/p</c> of nine words to the three
/// members. <see cref="SystemLineToParty" />: a system line to those three, as <see cref="PartyService.SendLine(Party, string)" />
/// sends a join, a leave or a handover. <see cref="LeaveInviteAccept" />: a party's leader leaves (the leadership passes, the party keeps two), the
/// new leader invites it back by name and it accepts: three events, the rosters and lines they send. The chat rate limit
/// is off (<c>Game:ChatMessagesPerMinute</c> 0), so every line is delivered however often it runs.
/// </summary>
[MemoryDiagnoser]
public class SocialEventBenchmarks
{
    private const int Players = 30;
    private const int PartySize = 3;

    private static readonly CharacterClass[] s_classes =
        [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Healer];

    private ScenarioWorld _world = null!;
    private PartyService _parties = null!;
    private ScenarioConnection[] _connections = null!;
    private ChatMessageHandler _chat = null!;
    private PartyLeaveHandler _leave = null!;
    private PartyInviteHandler _invite = null!;
    private PartyInviteResponseHandler _respond = null!;

    private readonly CPartyLeavePacket _leavePacket = new();
    private readonly CPartyInvitePacket _invitePacket = new();
    private readonly CPartyInviteResponsePacket _acceptPacket = new() { Accept = true };
    private readonly CChatMessagePacket _sayPacket = new() { Message = "Anyone heading out to the forest?" };
    private readonly CChatMessagePacket _partyPacket = new() { Message = "/p Ready when you are, see you at the gate." };
    private CChatMessagePacket _whisperPacket = null!;

    [GlobalSetup]
    public void Setup()
    {
        _world = ScenarioWorld.CreateWithReferenceData(new GameConfiguration { ChatMessagesPerMinute = 0 });
        MapInstance town = _world.AddInstance(MapType.Town);
        _parties = _world.Parties!;

        var navigator = new MapNavigator(NullLoggerFactory.Instance);
        navigator.LoadFromNavMesh(TownNavmesh.Shared);
        if (navigator.FindGround(TownNavmesh.LoopCentre, out Vector3 centre) != NavmeshGroundKind.Under)
            throw new InvalidOperationException("The loop's centre is off the town's navmesh");

        _connections = new ScenarioConnection[Players];
        for (int p = 0; p < Players; p++)
        {
            CharacterEntity character = _world.NewCharacter(s_classes[p % s_classes.Length]);
            character.Name = SocialSchedule.NameOf(p);
            _connections[p] = _world.Connect(character);
        }

        for (int p = 0; p < Players; p += PartySize)
            _world.FormParty(_connections[p..(p + PartySize)]);
        for (int p = 0; p < Players; p++)
            _world.Join(town, _connections[p], LoopWalker.LoopPoint(navigator, centre, p * MathF.Tau / Players));

        IOptions<GameConfiguration> options = Options.Create(_world.Host.Configuration);
        var limiter = new ChatRateLimiter(options, _world.Clock);
        var dispatcher = new CommandDispatcher(
            [new PartyChatCommand(_parties, limiter), new WhisperCommand(_parties.Online, limiter)],
            NullLogger<CommandDispatcher>.Instance);
        _chat = new ChatMessageHandler(_world.Host, dispatcher, limiter);
        _leave = new PartyLeaveHandler(_parties, NullLogger<PartyLeaveHandler>.Instance);
        _invite = new PartyInviteHandler(_parties, NullLogger<PartyInviteHandler>.Instance);
        _respond = new PartyInviteResponseHandler(_parties, NullLogger<PartyInviteResponseHandler>.Instance);

        // Player 0 whispers player 15, in party 5.
        _whisperPacket = new CChatMessagePacket { Message = $"/w {SocialSchedule.NameOf(15)} Meet me by the town gate." };

        // Each event once, checked by what it sent: a benchmark of an event that did nothing would measure nothing.
        Require(SaidLine, Players, "a said line reaches the town");
        Require(Whisper, 2, "a whisper reaches its target and echoes back");
        Require(PartyLine, PartySize, "a party line reaches the three members");
        Require(SystemLineToParty, PartySize, "a system line reaches the three members");
        LeaveInviteAccept();
        if (_parties.PartyOf(1)?.Members.Count != PartySize)
            throw new InvalidOperationException("The party is not whole again after its leader left and came back.");
    }

    [Benchmark]
    public void SaidLine() => Run(_chat, _connections[0], _sayPacket);

    [Benchmark]
    public void Whisper() => Run(_chat, _connections[0], _whisperPacket);

    [Benchmark]
    public void PartyLine() => Run(_chat, _connections[0], _partyPacket);

    /// <summary>A system line to party 0's three members, as a join, a leave or a handover sends one.</summary>
    [Benchmark]
    public void SystemLineToParty()
    {
        NetworkSendScheduler.DeferSignalsOnCurrentThread();
        try
        {
            _parties.SendLine(_parties.PartyOf(1)!, "Socialaa is now the party leader.");
            _world.Scheduler.SignalAll();
            _world.Scheduler.RunAllPasses();
        }
        finally
        {
            NetworkSendScheduler.ResumeSignalsOnCurrentThread();
        }
    }

    /// <summary>Party 0's turn: its leader leaves, the new leader invites it back and it accepts, joining last.</summary>
    [Benchmark]
    public void LeaveInviteAccept()
    {
        Party party = _parties.PartyOf(1)!;
        ScenarioConnection leaver = _connections[party.Leader.Value - 1];
        Run(_leave, leaver, _leavePacket);

        ScenarioConnection leader = _connections[party.Leader.Value - 1];
        _invitePacket.TargetName = leaver.Character!.Name;
        Run(_invite, leader, _invitePacket);
        Run(_respond, leaver, _acceptPacket);
    }

    /// <summary>The handler, then the send pass, as a tick runs them: the sends wake no thread until the pass.</summary>
    private void Run<T>(WorldPacketHandler<T> handler, ScenarioConnection connection, T packet) where T : Packet
    {
        NetworkSendScheduler.DeferSignalsOnCurrentThread();
        try
        {
            handler.Execute(connection, packet);
            _world.Scheduler.SignalAll();
            _world.Scheduler.RunAllPasses();
        }
        finally
        {
            NetworkSendScheduler.ResumeSignalsOnCurrentThread();
        }
    }

    private void Require(Action run, int packets, string what)
    {
        int before = 0, after = 0;
        foreach (ScenarioConnection connection in _connections)
            before += connection.Sent;
        run();
        foreach (ScenarioConnection connection in _connections)
            after += connection.Sent;
        if (after - before != packets)
            throw new InvalidOperationException($"Not as expected: {what} ({after - before} packets, not {packets}).");
    }

    [GlobalCleanup]
    public void Cleanup() => _world.Dispose();
}

using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using Avalon.LoadTest.Wire;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Party;

namespace Avalon.LoadTest.Bots;

/// <summary>One member of a party as its formation drives it: a <see cref="Bot"/>, or a test's stand-in.</summary>
public interface IPartyMember
{
    /// <summary>The character's name, by which the leader invites it (a bot's is its account's).</summary>
    string Name { get; }

    /// <summary>Whether the character is in the world, where a party request is accepted.</summary>
    bool InWorld { get; }

    /// <summary>The member's connection, counted: a roster read on an earlier connection says nothing of the current one.</summary>
    int Generation { get; }

    /// <summary>
    /// Sends <paramref name="message"/> on the member's current connection; an <see cref="InvalidOperationException"/>
    /// when it is not in the world.
    /// </summary>
    ValueTask SendAsync<T>(T message, NetworkPacketType type, CancellationToken ct) where T : class;
}

/// <summary>Where a party of fighters stands.</summary>
public enum PartyState
{
    /// <summary>Being formed (<see cref="PartyFormer"/>): its members stand in town.</summary>
    Forming,

    /// <summary>Formed: each member sets out once its own roster lists the whole party.</summary>
    Formed,

    /// <summary>It failed to form twice, or fell apart once formed: its members fight solo.</summary>
    Solo,
}

/// <summary>
/// A party of fighters (<c>--party-size</c>, 2 to <see cref="MaxSize"/>): its members in join order, the first its
/// leader, and each member's <see cref="PartyLink"/>, which its bot's connections hand the party packets to. Formed with
/// the real party packets by <see cref="PartyFormer"/>; its members then walk to the portal together and share the
/// party's forest, each fighting on its own.
/// </summary>
/// <remarks>
/// The world keeps a party in memory while it runs, a member who logs out included (shown offline), so a member that
/// reconnects (a fighter's own ask, a churn of the connection, a disconnect) is still in it when it is back: the world
/// sends it the roster as its character spawns, and the member sets out once that roster lists the whole party. A
/// member that gave up for good stays on the roster, offline, and holds nobody back. A roster that does not list the
/// whole party on the member's current connection within <see cref="PartyLink.RosterWait"/> (the world restarted and
/// forgot every party, or a member was removed) means the party fell apart: counted once
/// (<see cref="BotMetrics.PartyFormFailed"/>, <c>party:fell-apart</c>), every member leaves what is left of it, and they
/// fight solo from then on. No member ever waits longer than that for a roster.
/// </remarks>
public sealed class BotParty
{
    /// <summary>The largest party: the world's <c>Game:MaxPartySize</c> and the forest's seats.</summary>
    public const int MaxSize = 6;

    private readonly BotMetrics _metrics;
    private readonly long _departJitter;
    private int _state = (int)PartyState.Forming;
    private long _departAt;

    /// <param name="members">The members in join order, the first the leader; 2 to <see cref="MaxSize"/>.</param>
    /// <param name="departJitter">
    /// Once formed (or solo), the party sets out from town after a random wait up to this, all its members at once:
    /// <see cref="Fighter.FirstTripJitter"/> in a ramp, so forest bakes do not arrive at once; 0 for <c>check</c>.
    /// </param>
    public BotParty(IReadOnlyList<IPartyMember> members, BotMetrics metrics, TimeSpan departJitter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(members.Count, 2, nameof(members));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(members.Count, MaxSize, nameof(members));
        ArgumentOutOfRangeException.ThrowIfLessThan(departJitter, TimeSpan.Zero);
        _metrics = metrics;
        _departJitter = (long)(departJitter.TotalSeconds * Stopwatch.Frequency);
        Members = members;
        Links = [.. members.Select(member => new PartyLink(this, member))];
    }

    /// <summary>The members in join order; the first is the leader.</summary>
    public IReadOnlyList<IPartyMember> Members { get; }

    /// <summary>Each member's link, in the same order: set it as the member bot's <see cref="Bot.Party"/>.</summary>
    public IReadOnlyList<PartyLink> Links { get; }

    public PartyState State => (PartyState)Volatile.Read(ref _state);

    /// <summary>When the members may set out from town (<see cref="Stopwatch.GetTimestamp"/>), once formed or solo.</summary>
    public long DepartAt => Volatile.Read(ref _departAt);

    /// <summary>Called with a line about a failed attempt or the party falling apart (for <c>check</c>).</summary>
    public Action<string>? Note { get; set; }

    /// <summary>Whether a roster's members are exactly this party's, by name (the world ignores a name's case).</summary>
    internal bool IsWhole(List<PartyMemberDto> roster)
    {
        if (roster.Count != Members.Count) return false;

        foreach (IPartyMember member in Members)
        {
            if (!roster.Exists(entry => string.Equals(entry.Name, member.Name, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    /// <summary>The leader's roster lists every member: formed, setting out after the party's wait.</summary>
    internal void Formed()
    {
        Depart();
        Volatile.Write(ref _state, (int)PartyState.Formed);
        _metrics.PartyFormed();
    }

    /// <summary>Failed to form twice: counted by <paramref name="reason"/>, and the members fight solo.</summary>
    internal void GoSolo(string reason)
    {
        _metrics.PartyFormFailed(reason);
        Depart();
        Volatile.Write(ref _state, (int)PartyState.Solo);
    }

    /// <summary>
    /// A member's roster has not listed the whole party for <see cref="PartyLink.RosterWait"/>: once per party, counted,
    /// every member leaves what is left of it (off the asking thread, the input driver's), and they fight solo.
    /// </summary>
    internal void FallApart()
    {
        if (Interlocked.CompareExchange(ref _state, (int)PartyState.Solo, (int)PartyState.Formed) != (int)PartyState.Formed)
            return;

        _metrics.PartyFormFailed("party:fell-apart");
        Note?.Invoke("The party fell apart (a roster without every member); its members fight solo.");
        _ = Task.Run(() => PartyFormer.LeaveAllAsync(Members, CancellationToken.None));
    }

    private void Depart() => Volatile.Write(ref _departAt, Stopwatch.GetTimestamp() + (long)(Random.Shared.NextDouble() * _departJitter));
}

/// <summary>What a party member's connection handed over while its party was being formed.</summary>
internal enum PartyEventKind { Invite, Result, Roster }

/// <param name="Name">The inviter's name on an invite; the other character on a result, when the world names one.</param>
internal readonly record struct PartyEvent(PartyEventKind Kind, PartyResult Result, string? Name);

/// <summary>
/// One member's place in its <see cref="BotParty"/>: the party packets its bot's connections hand over (on their read
/// loops), its last roster, and the gate its fighter asks before it sets out from town (<see cref="ReadyToLeave"/>).
/// </summary>
public sealed class PartyLink
{
    /// <summary>The longest a formed party's member waits for a roster listing the whole party before the party falls apart.</summary>
    public static readonly TimeSpan RosterWait = TimeSpan.FromSeconds(20);

    private const long NoRoster = -1;

    private static readonly long s_rosterWait = (long)(RosterWait.TotalSeconds * Stopwatch.Frequency);

    /// <summary>What the connection handed over while the party is formed; nothing is kept otherwise.</summary>
    private readonly Channel<PartyEvent> _events = Channel.CreateBounded<PartyEvent>(new BoundedChannelOptions(64)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private volatile bool _listening;

    /// <summary>The last roster read: <c>generation &lt;&lt; 1</c>, and 1 when it listed the whole party.</summary>
    private long _roster = NoRoster;

    /// <summary>Since when the gate has found no whole roster on the current connection; 0 while it has one. The driver's only.</summary>
    private long _waitingSince;

    internal PartyLink(BotParty party, IPartyMember member)
    {
        Party = party;
        Member = member;
    }

    public BotParty Party { get; }

    public IPartyMember Member { get; }

    /// <summary>Whether the last roster read was on connection <paramref name="generation"/> and listed the whole party.</summary>
    public bool RosterWhole(int generation) => Volatile.Read(ref _roster) == (((long)generation << 1) | 1);

    /// <summary>
    /// A party packet the member's connection <paramref name="generation"/> read, on its read loop: a roster is always
    /// read (the gate needs it); an invite and a result only while the party is being formed.
    /// </summary>
    public void OnPacket(NetworkPacket packet, PacketCodec codec, int generation)
    {
        switch (packet.Header.Type)
        {
            case NetworkPacketType.SMSG_PARTY_ROSTER:
                bool whole = Party.IsWhole(codec.Decode<SPartyRosterPacket>(packet).Members);
                Volatile.Write(ref _roster, ((long)generation << 1) | (whole ? 1L : 0L));
                if (_listening) _events.Writer.TryWrite(new PartyEvent(PartyEventKind.Roster, PartyResult.Unknown, null));
                break;
            case NetworkPacketType.SMSG_PARTY_INVITE when _listening:
                _events.Writer.TryWrite(new PartyEvent(PartyEventKind.Invite, PartyResult.Unknown,
                    codec.Decode<SPartyInvitePacket>(packet).InviterName));
                break;
            case NetworkPacketType.SMSG_PARTY_RESULT when _listening:
                SPartyResultPacket result = codec.Decode<SPartyResultPacket>(packet);
                _events.Writer.TryWrite(new PartyEvent(PartyEventKind.Result, result.Result, result.Name));
                break;
        }
    }

    /// <summary>
    /// Whether the member's fighter may set out from town at <paramref name="now"/>, on the driver's thread: never while
    /// the party is being formed; once formed, when the roster on the member's current connection lists the whole party
    /// and the party's departure has come; solo, when the departure has come. A formed party whose roster this member has
    /// not seen whole for <see cref="RosterWait"/> falls apart (<see cref="BotParty.FallApart"/>). Allocates nothing.
    /// </summary>
    public bool ReadyToLeave(long now)
    {
        switch (Party.State)
        {
            case PartyState.Forming:
                return false;
            case PartyState.Solo:
                return now >= Party.DepartAt;
        }

        if (RosterWhole(Member.Generation))
        {
            _waitingSince = 0;
            return now >= Party.DepartAt;
        }

        if (_waitingSince == 0)
        {
            _waitingSince = now;
            return false;
        }

        if (now - _waitingSince <= s_rosterWait) return false;

        _waitingSince = 0;
        Party.FallApart();
        return now >= Party.DepartAt;
    }

    /// <summary>Keeps what the connection hands over from now on, and drops what was kept.</summary>
    internal void Listen()
    {
        while (_events.Reader.TryRead(out _))
        {
        }

        _listening = true;
    }

    /// <summary>Stops keeping it, and drops what is kept.</summary>
    internal void StopListening()
    {
        _listening = false;
        while (_events.Reader.TryRead(out _))
        {
        }
    }

    /// <summary>The next thing the connection handed over.</summary>
    internal ValueTask<PartyEvent> NextAsync(CancellationToken ct) => _events.Reader.ReadAsync(ct);
}

/// <summary>
/// Forms a <see cref="BotParty"/> with the party packets a player's client sends: the leader invites each member by
/// character name (<c>CMSG_PARTY_INVITE</c>), each member answers its <c>SMSG_PARTY_INVITE</c> with an accept
/// (<c>CMSG_PARTY_INVITE_RESPONSE</c>), and the party is formed once the leader's <c>SMSG_PARTY_ROSTER</c> lists every
/// member. Each request is answered with exactly one <c>SMSG_PARTY_RESULT</c>, after any roster it caused.
/// </summary>
/// <remarks>
/// An attempt starts from scratch: every member, the leader last, declines an invite it may hold and leaves a party it
/// may be in (<c>CMSG_PARTY_LEAVE</c>), whether of an earlier attempt or of an earlier run (the world keeps a party, its
/// offline members in it, while it runs). An attempt fails on any refusal, on an invite that ends unanswered
/// (<c>InviteExpired</c>, <c>InviteDeclined</c>), on a member not in the world, or after <see cref="Timeout"/>; a failed
/// attempt is tried once more, and a second failure is counted (<see cref="BotMetrics.PartyFormFailed"/>), every member
/// leaves what was formed, and the members fight solo.
/// </remarks>
public static class PartyFormer
{
    /// <summary>One attempt's time, the wait for every member to be in the world included.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>How often an attempt looks whether every member is in the world, until they are.</summary>
    private static readonly TimeSpan s_inWorldPoll = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Forms <paramref name="party"/>: true once formed (<see cref="PartyState.Formed"/>); false, the party
    /// <see cref="PartyState.Solo"/>, when both attempts failed. Never throws but for a cancel of <paramref name="ct"/>,
    /// which leaves the party forming.
    /// </summary>
    public static async Task<bool> FormAsync(BotParty party, CancellationToken ct)
    {
        string failure = "party:unknown";
        try
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                // What the last attempt left unread answers nothing of this one.
                foreach (PartyLink link in party.Links) link.Listen();
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(Timeout);
                try
                {
                    await AttemptAsync(party, limit.Token);
                    party.Formed();
                    return true;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // A member still entering (its entry failed, its life loop retries after a pause) is the likelier cause.
                    failure = party.Members.All(member => member.InWorld) ? "party:timeout" : "party:not-in-world";
                }
                catch (PartyFormationException error)
                {
                    failure = error.Kind;
                }
                catch (InvalidOperationException)
                {
                    // A member left the world between the look and the send.
                    failure = "party:not-in-world";
                }
                catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException)
                {
                    failure = "party:io";
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    // Never left forming: its members would stand in town for good.
                    failure = "party:unexpected";
                }

                party.Note?.Invoke(attempt == 1
                    ? $"Forming the party failed ({failure}); trying again from scratch."
                    : $"Forming the party failed again ({failure}); its members fight solo.");
            }
        }
        finally
        {
            foreach (PartyLink link in party.Links) link.StopListening();
        }

        // Whatever the attempts formed is left before the members set out alone.
        await LeaveAllAsync(party.Members, ct);
        party.GoSolo(failure);
        return false;
    }

    /// <summary>Every member in the world sends <c>CMSG_PARTY_LEAVE</c>, best effort: its answer is not waited for.</summary>
    internal static async Task LeaveAllAsync(IReadOnlyList<IPartyMember> members, CancellationToken ct)
    {
        foreach (IPartyMember member in members)
        {
            try
            {
                if (member.InWorld) await member.SendAsync(new CPartyLeavePacket(), NetworkPacketType.CMSG_PARTY_LEAVE, ct);
            }
            catch (Exception)
            {
                // Out of the world or closing: a member that is not there is in no party the others share a forest with.
            }
        }
    }

    private static async Task AttemptAsync(BotParty party, CancellationToken ct)
    {
        // A party request is accepted only in the world; a member entering again is waited for, within the attempt.
        while (!party.Members.All(member => member.InWorld)) await Task.Delay(s_inWorldPoll, ct);

        // From scratch, the leader last: the unasked InviteDeclined a member's decline sends the leader arrives before the
        // leader's own answers, which are then read past it.
        IReadOnlyList<PartyLink> links = party.Links;
        for (int i = links.Count - 1; i >= 0; i--)
        {
            PartyResult declined = await RequestAsync(links[i], new CPartyInviteResponsePacket { Accept = false },
                NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE, unaskedFails: false, ct);
            if (declined is not (PartyResult.Ok or PartyResult.NoInvite)) throw new PartyFormationException($"party:decline:{declined}");

            PartyResult left = await RequestAsync(links[i], new CPartyLeavePacket(), NetworkPacketType.CMSG_PARTY_LEAVE,
                unaskedFails: false, ct);
            if (left is not (PartyResult.Ok or PartyResult.NotInParty)) throw new PartyFormationException($"party:leave:{left}");
        }

        PartyLink leader = links[0];
        for (int i = 1; i < links.Count; i++)
        {
            PartyLink member = links[i];
            PartyResult invited = await RequestAsync(leader, new CPartyInvitePacket { TargetName = member.Member.Name },
                NetworkPacketType.CMSG_PARTY_INVITE, unaskedFails: true, ct);
            if (invited != PartyResult.Ok) throw new PartyFormationException($"party:invite:{invited}");

            // The world sends the invite before it answers the leader: it is there, or on its way.
            while (true)
            {
                PartyEvent next = await member.NextAsync(ct);
                if (next.Kind == PartyEventKind.Invite && string.Equals(next.Name, leader.Member.Name, StringComparison.OrdinalIgnoreCase))
                    break;

                ThrowIfInviteEnded(next);
            }

            PartyResult accepted = await RequestAsync(member, new CPartyInviteResponsePacket { Accept = true },
                NetworkPacketType.CMSG_PARTY_INVITE_RESPONSE, unaskedFails: true, ct);
            if (accepted != PartyResult.Ok) throw new PartyFormationException($"party:accept:{accepted}");
        }

        // Each join sends every member the roster; the last one lists the whole party.
        while (!leader.RosterWhole(leader.Member.Generation)) ThrowIfInviteEnded(await leader.NextAsync(ct));
    }

    /// <summary>
    /// Sends a party request and returns its answer: the next result that is not an unasked one. An unasked
    /// <c>InviteExpired</c> or <c>InviteDeclined</c> on the way fails the attempt when <paramref name="unaskedFails"/>,
    /// and is read past otherwise (starting from scratch, they answer what is being undone).
    /// </summary>
    private static async Task<PartyResult> RequestAsync<T>(PartyLink link, T message, NetworkPacketType type, bool unaskedFails,
        CancellationToken ct) where T : class
    {
        await link.Member.SendAsync(message, type, ct);
        while (true)
        {
            PartyEvent next = await link.NextAsync(ct);
            if (next.Kind != PartyEventKind.Result) continue;

            if (IsUnasked(next.Result))
            {
                if (unaskedFails) ThrowIfInviteEnded(next);
                continue;
            }

            return next.Result;
        }
    }

    private static bool IsUnasked(PartyResult result) => result is PartyResult.InviteExpired or PartyResult.InviteDeclined;

    /// <summary>An invite of the attempt ended unanswered: it failed.</summary>
    private static void ThrowIfInviteEnded(PartyEvent next)
    {
        if (next.Kind == PartyEventKind.Result && IsUnasked(next.Result))
            throw new PartyFormationException($"party:{next.Result}");
    }

    /// <summary>An attempt failed; <see cref="Kind"/> names how, as counted.</summary>
    private sealed class PartyFormationException(string kind) : Exception(kind)
    {
        public string Kind { get; } = kind;
    }
}

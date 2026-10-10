using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.State;
using Avalon.World.Characters;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Avalon.World.Respawn;
using Avalon.World.Social;
using Avalon.World.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avalon.World.Parties;

/// <summary>
/// Every party on this world server (spec 2026-09-30 section 1): in memory only, so a restart disbands them all.
/// Tick thread only, every method: the handlers, the chat commands, World's spawn, transfer and despawn and
/// World.Update all call it on the tick. The one exception is the shutdown despawn, which WorldServer.OnStoppingAsync
/// runs on its own thread once the tick thread has ended (the tick unbinds the guard as it ends), joined for at most
/// 5 seconds: a tick still running past that bound could overlap it, and with the guard on (#639) that despawn is
/// refused, logged and skipped for that one character while the others still despawn. Deadlines are read from the
/// container's TimeProvider inside those calls; there are no timers. The mutators assert the tick thread while
/// <see cref="TickThreadGuard" /> is enabled (#639).
/// </summary>
public sealed class PartyService(IOptions<GameConfiguration> options, TimeProvider time, ILogger<PartyService> logger,
    OnlineCharacters? online = null, TickThreadGuard? tickThread = null)
{
    // Who is online is shared with the whisper (#717): the service keeps feeding it from the world's online and
    // offline hooks. Production registers one singleton (WorldHostGraphShould pins that the service gets it).
    private readonly OnlineCharacters _onlineCharacters = online ?? new OnlineCharacters(tickThread);

    private readonly Dictionary<uint, Party> _parties = [];
    private readonly Dictionary<uint, Party> _partyOf = [];
    private readonly Dictionary<uint, PartyInvite> _invites = [];   // by invitee

    // Invites to a character that ignores the inviter (#723), by (inviter, target): never sent to the target and never
    // answerable, but kept until they expire so the inviter sees exactly what a sent invite shows.
    private readonly Dictionary<(uint Inviter, uint Target), PartyInvite> _droppedInvites = [];
    private readonly List<(uint Inviter, uint Target)> _droppedScratch = [];
    private readonly Dictionary<uint, ushort> _lastLevel = [];
    private readonly List<uint> _scratch = [];
    private readonly List<IWorldConnection> _due = [];
    private readonly Dictionary<uint, (MemberStatus Status, DateTimeOffset At)> _statusSent = [];

    /// <summary>At most this often per member (four times a second).</summary>
    public static readonly TimeSpan StatusInterval = TimeSpan.FromMilliseconds(250);

    // Second marks the countdown is announced at, highest first (spec 2026-09-30 section 2).
    private static readonly int[] s_countdownMarks = [60, 30, 10, 5, 4, 3, 2, 1];
    private readonly Dictionary<uint, PartyCountdown> _countdowns = [];
    private readonly Dictionary<uint, PendingReturn> _returning = [];   // countdown ran out, return under way (#700)

    /// <summary>
    /// How many times a countdown's return to town that failed is tried again, <c>PartyReturnRetrySeconds</c> apart,
    /// before it is given up (#700).
    /// </summary>
    public const int MaxReturnRetries = 5;
    private IPartyInstanceRegistry _instances = NoPartyInstances.Instance;
    private uint _lastPartyId;

    private GameConfiguration Config => options.Value;

    /// <summary>World calls this once its registry exists.</summary>
    public void AttachInstances(IPartyInstanceRegistry instances) => _instances = instances;

    public Party? PartyOf(uint characterId) => _partyOf.GetValueOrDefault(characterId);

    /// <summary>Who is online, shared with the whisper (#717).</summary>
    public OnlineCharacters Online => _onlineCharacters;

    public IWorldConnection? OnlineConnection(uint characterId) => _onlineCharacters.ById(characterId);

    /// <summary>The online character with this name, ignoring case and surrounding spaces, as an invite looks it up.</summary>
    public IWorldConnection? OnlineConnectionByName(string name) => _onlineCharacters.ByName(name);

    /// <summary>The character moved to another instance (World.TransferPlayer): who shares an instance changed for its whole party.</summary>
    public void InstanceChanged(IWorldConnection connection)
    {
        tickThread?.AssertOnTick("PartyService.InstanceChanged");
        // A return under way that moved the character is done: a failure reported after the move is not retried.
        if (connection.Character is { } moved)
            _returning.Remove(moved.Guid.Id);

        if (connection.Character is { } character && PartyOf(character.Guid.Id) is { } party)
        {
            ResetStatuses(party);
            SendRoster(party);
        }
    }

    /// <summary>The character entered the world (World.SpawnInInstance).</summary>
    public void CharacterOnline(IWorldConnection connection)
    {
        tickThread?.AssertOnTick("PartyService.CharacterOnline");
        if (connection.Character is not { } character)
            return;

        uint id = character.Guid.Id;
        _onlineCharacters.Add(connection);
        _lastLevel[id] = character.Level;

        Party? party = PartyOf(id);
        if (character is CharacterEntity entity)
            entity.PartyId = party?.Id;

        if (party is not null)
        {
            ResetStatuses(party);
            SendRoster(party);
        }
    }

    /// <summary>The character left the world (World.LeaveWorldAsync, before the connection lets go of it).</summary>
    public void CharacterOffline(IWorldConnection connection, ICharacter character)
    {
        tickThread?.AssertOnTick("PartyService.CharacterOffline");
        uint id = character.Guid.Id;
        if (!_onlineCharacters.Remove(connection, character))
            return;

        _statusSent.Remove(id);
        _lastLevel[id] = character.Level;

        EndInvitesOf(id);
        OnCharacterOffline(id);

        if (PartyOf(id) is not { } party)
            return;

        if (party.IsLeader(id))
            HandOverLeadership(party);

        SendRoster(party);
    }

    public PartyResult Invite(uint inviterId, string targetName)
    {
        tickThread?.AssertOnTick("PartyService.Invite");
        if (OnlineConnection(inviterId)?.Character is not { } inviter)
            return PartyResult.NotFound;

        Party? party = PartyOf(inviterId);
        if (party is not null && !party.IsLeader(inviterId))
            return PartyResult.NotLeader;

        if (!_onlineCharacters.TryIdByName(targetName, out uint targetId)
            || OnlineConnection(targetId) is not { Character: { } target } targetConnection)
        {
            return PartyResult.NotFound;
        }

        if (targetId == inviterId)
            return PartyResult.Self;
        if (_partyOf.ContainsKey(targetId))
            return PartyResult.AlreadyInParty;
        if (_invites.ContainsKey(targetId) || _droppedInvites.ContainsKey((inviterId, targetId)))
            return PartyResult.InvitePending;
        if (party is not null && party.Members.Count >= Config.MaxPartySize)
            return PartyResult.PartyFull;

        var timeout = TimeSpan.FromSeconds(Config.PartyInviteTimeoutSeconds);
        var invite = new PartyInvite(inviterId, inviter.Name, target.Name, party?.Id, time.GetUtcNow() + timeout);

        // A target ignoring the inviter never sees the invite (#723). Only after every refusal, so the inviter is
        // answered exactly as for a sent invite, and later told it expired as a sent one would be.
        if (Ignoring.Hides(targetConnection, inviterId))
        {
            _droppedInvites[(inviterId, targetId)] = invite;
            return PartyResult.Ok;
        }

        _invites[targetId] = invite;
        targetConnection.Send(SPartyInvitePacket.Create(inviter.Name, (ushort)inviter.Class, inviter.Level,
            (uint)timeout.TotalMilliseconds, PacketEncoder.Shared));
        return PartyResult.Ok;
    }

    /// <summary>
    /// The target has just ignored the inviter (#723, owner decision): an invite from it the target still holds ends
    /// silently for the target, which can no longer accept it, and becomes a dropped invite, so the inviter is told
    /// nothing new and sees it expire as before. Any other invite the target holds is left alone.
    /// </summary>
    public void HideInviteFrom(uint targetId, uint inviterId)
    {
        tickThread?.AssertOnTick("PartyService.HideInviteFrom");
        if (_invites.TryGetValue(targetId, out PartyInvite? held) && held.InviterId == inviterId)
        {
            _invites.Remove(targetId);
            _droppedInvites[(inviterId, targetId)] = held;
        }
    }

    public PartyResult Respond(uint targetId, bool accept)
    {
        tickThread?.AssertOnTick("PartyService.Respond");
        if (!_invites.Remove(targetId, out PartyInvite? invite))
            return PartyResult.NoInvite;

        if (OnlineConnection(targetId)?.Character is not { } target)
            return PartyResult.NotFound;

        if (!accept)
        {
            Tell(invite.InviterId, PartyResult.InviteDeclined, target.Name);
            return PartyResult.Ok;
        }

        if (_partyOf.ContainsKey(targetId))
            return PartyResult.AlreadyInParty;

        Party? party = PartyForInvite(invite);
        if (party is null)
            return PartyResult.NotFound;
        if (party.Members.Count >= Config.MaxPartySize)
            return PartyResult.PartyFull;

        Join(party, target);
        return PartyResult.Ok;
    }

    public PartyResult Leave(uint memberId)
    {
        tickThread?.AssertOnTick("PartyService.Leave");
        if (PartyOf(memberId) is not { } party)
            return PartyResult.NotInParty;

        RemoveMember(party, memberId, PartyLeaveReason.Left);
        return PartyResult.Ok;
    }

    public PartyResult Kick(uint leaderId, uint targetId)
    {
        tickThread?.AssertOnTick("PartyService.Kick");
        if (PartyOf(leaderId) is not { } party)
            return PartyResult.NotInParty;
        if (!party.IsLeader(leaderId))
            return PartyResult.NotLeader;
        if (targetId == leaderId)
            return PartyResult.Self;
        if (party.Find(targetId) is null)
            return PartyResult.NotFound;

        RemoveMember(party, targetId, PartyLeaveReason.Kicked);
        return PartyResult.Ok;
    }

    public PartyResult Promote(uint leaderId, uint targetId)
    {
        tickThread?.AssertOnTick("PartyService.Promote");
        if (PartyOf(leaderId) is not { } party)
            return PartyResult.NotInParty;
        if (!party.IsLeader(leaderId))
            return PartyResult.NotLeader;
        if (targetId == leaderId)
            return PartyResult.Self;
        // An offline member is refused as one not in the party: Tick hands leadership straight back from an offline
        // leader, so the promote would only bounce.
        if (party.Find(targetId) is not { } member || OnlineConnection(targetId) is null)
            return PartyResult.NotFound;

        party.Leader = member.Id;
        SendRoster(party);
        SendLine(party, $"{member.Name} is now the party leader.");
        return PartyResult.Ok;
    }

    public PartyResult SetExperienceMode(uint leaderId, PartyExperienceMode mode)
    {
        tickThread?.AssertOnTick("PartyService.SetExperienceMode");
        if (PartyOf(leaderId) is not { } party)
            return PartyResult.NotInParty;
        if (!party.IsLeader(leaderId))
            return PartyResult.NotLeader;
        if (mode is not (PartyExperienceMode.Even or PartyExperienceMode.LevelWeighted))
            return PartyResult.Invalid;

        DateTimeOffset now = time.GetUtcNow();
        if (party.ExperienceModeLockedUntil is { } lockedUntil && now < lockedUntil)
            return PartyResult.OnCooldown;

        foreach (PartyMember member in party.Members)
        {
            if (OnlineConnection(member.Id.Value)?.Character is { IsInCombat: true })
                return PartyResult.InCombat;
        }

        if (party.ExperienceMode == mode)
            return PartyResult.Ok;

        party.SetExperienceMode(mode, now + TimeSpan.FromSeconds(Config.PartyExperienceModeCooldownSeconds));
        SendRoster(party);
        SendLine(party, mode == PartyExperienceMode.Even
            ? "Experience is now shared evenly."
            : "Experience is now shared by level.");
        return PartyResult.Ok;
    }

    /// <summary>
    /// Once per tick, from World.Update: invite expiry, leadership away from an offline leader, and the leave
    /// countdowns. Returns the connections whose countdown ended while still in the party's instance, for World
    /// to move to town. The list is reused: read it before the next call.
    /// </summary>
    public IReadOnlyList<IWorldConnection> Tick()
    {
        tickThread?.AssertOnTick("PartyService.Tick");
        DateTimeOffset now = time.GetUtcNow();
        ExpireInvites(now);

        foreach (Party party in _parties.Values)
        {
            if (!_onlineCharacters.IsOnline(party.Leader.Value))
                HandOverLeadership(party);
        }

        _due.Clear();
        AdvanceCountdowns(now, _due);
        return _due;
    }

    public void SendLine(uint characterId, string text)
    {
        if (OnlineConnection(characterId) is { } connection)
            connection.Send(SChatMessagePacket.System(text, time.GetUtcNow().UtcDateTime, PacketEncoder.Shared));
    }

    public void SendLine(Party party, string text)
    {
        foreach (PartyMember member in party.Members)
            SendLine(member.Id.Value, text);
    }

    public bool InCountdown(uint characterId) => _countdowns.ContainsKey(characterId);

    /// <summary>
    /// From World.Update, with what <see cref="Tick" /> returned: starts each connection's return to town (alive or
    /// dead; a dead one arrives revived) and has a failed one tried again (#700). A connection already returning (a
    /// respawn in flight) is not started twice: its countdown is re-armed as if the return had failed, so it is
    /// checked again once that settles. Each start is contained.
    /// </summary>
    public void StartReturns(IReadOnlyList<IWorldConnection> due, TownReturn town)
    {
        tickThread?.AssertOnTick("PartyService.StartReturns");
        for (int i = 0; i < due.Count; i++)
        {
            IWorldConnection connection = due[i];
            if (connection.Character is not { } character)
                continue;

            uint id = character.Guid.Id;
            if (connection.RespawnInFlight)
            {
                ReturnFailed(id, new InvalidOperationException("A return to town was already under way"));
                continue;
            }

            try
            {
                connection.RespawnInFlight = true;
                town.Start(connection, revive: character.IsDead, dropEncounter: true, failed: e => ReturnFailed(id, e));
            }
            catch (Exception e)
            {
                connection.RespawnInFlight = false;
                ReturnFailed(id, e);
            }
        }
    }

    /// <summary>
    /// A countdown's return to town failed (#700). Retried after <c>PartyReturnRetrySeconds</c>, logged at Warning,
    /// while the character is still online, still inside the instance its countdown was for, and not back in that
    /// party; at most <see cref="MaxReturnRetries" /> times, and the last failure is logged at Error and given up, as
    /// is a failure that will not be retried. A retry says nothing to the player: its countdown already reached zero.
    /// </summary>
    public void ReturnFailed(uint characterId, Exception failure)
    {
        tickThread?.AssertOnTick("PartyService.ReturnFailed");
        if (!_returning.Remove(characterId, out PendingReturn? pending))
        {
            logger.LogError(failure, "Return to town of character {CharacterId} failed; not retried: it is no longer returning",
                characterId);
            return;
        }

        if (OnlineConnection(characterId)?.Character is not { } character || character.InstanceId != pending.InstanceId
            || PartyOf(characterId)?.Id.Equals(pending.Party) == true)
        {
            logger.LogError(failure,
                "Return to town of character {CharacterId} failed; not retried: it went offline, left instance {InstanceId} or rejoined its party",
                characterId, pending.InstanceId);
            return;
        }

        int attempt = pending.Retries + 1;
        if (pending.Retries >= MaxReturnRetries)
        {
            logger.LogError(failure,
                "Gave up returning character {CharacterId} to town after {Attempts} attempts; it stays in instance {InstanceId}",
                characterId, attempt, pending.InstanceId);
            return;
        }

        int delay = Config.PartyReturnRetrySeconds;
        _countdowns[characterId] = new PartyCountdown(pending.InstanceId, pending.Party,
            time.GetUtcNow() + TimeSpan.FromSeconds(delay))
        { NextMark = s_countdownMarks.Length, Retries = attempt };
        logger.LogWarning(failure,
            "Return to town of character {CharacterId} failed (attempt {Attempt} of {Attempts}); trying again in {Seconds} s",
            characterId, attempt, MaxReturnRetries + 1, delay);
    }

    /// <summary>A member levelled up: the roster shows levels.</summary>
    public void LevelChanged(ICharacter character)
    {
        tickThread?.AssertOnTick("PartyService.LevelChanged");
        if (PartyOf(character.Guid.Id) is { } party)
            SendRoster(party);
    }

    /// <summary>
    /// Once per tick, from WorldServer.Update after both passes: each online member whose pools changed, and who was
    /// not sent within <see cref="StatusInterval" />, is sent to the other online members in its instance. A change
    /// held back by the interval goes on the first flush after it, if it still differs from what was sent.
    /// </summary>
    public void FlushMemberStatus()
    {
        tickThread?.AssertOnTick("PartyService.FlushMemberStatus");
        DateTimeOffset now = time.GetUtcNow();
        foreach (Party party in _parties.Values)
        {
            for (int i = 0; i < party.Members.Count; i++)
            {
                uint id = party.Members[i].Id.Value;
                if (OnlineConnection(id)?.Character is not { } character)
                    continue;

                var status = MemberStatus.Of(character);
                if (_statusSent.TryGetValue(id, out (MemberStatus Status, DateTimeOffset At) last)
                    && (last.Status == status || now - last.At < StatusInterval))
                {
                    continue;
                }

                _statusSent[id] = (status, now);
                for (int j = 0; j < party.Members.Count; j++)
                {
                    uint other = party.Members[j].Id.Value;
                    if (other == id || OnlineConnection(other) is not { Character: { } watcher } connection
                        || watcher.InstanceId != character.InstanceId)
                    {
                        continue;
                    }

                    connection.Send(SPartyMemberStatusPacket.Create(id, status.Health, status.MaxHealth, status.Power,
                        status.MaxPower, status.PowerType, status.IsDead, PacketEncoder.Shared));
                }
            }
        }
    }

    /// <summary>Who shares an instance changed: every member is sent afresh on the next flush.</summary>
    private void ResetStatuses(Party party)
    {
        foreach (PartyMember member in party.Members)
            _statusSent.Remove(member.Id.Value);
    }

    private void OnCharacterOffline(uint characterId)
    {
        _countdowns.Remove(characterId);
        _returning.Remove(characterId);
    }

    /// <summary>
    /// A character stopped being a member. Inside that party's instance it gets the leave countdown and is told why
    /// in the same line; returns whether it did, so a disband does not tell it twice.
    /// </summary>
    private bool OnRemovedFromParty(uint characterId, Party party, PartyLeaveReason reason)
    {
        if (OnlineConnection(characterId)?.Character is not { } character
            || !_instances.IsPartyInstance(party.Id, character.InstanceId))
        {
            return false;
        }

        int grace = Config.PartyLeaveGraceSeconds;
        int next = 0;
        while (next < s_countdownMarks.Length && s_countdownMarks[next] >= grace)
            next++;

        _countdowns[characterId] = new PartyCountdown(character.InstanceId, party.Id,
            time.GetUtcNow() + TimeSpan.FromSeconds(grace))
        { NextMark = next };
        _returning.Remove(characterId);

        string why = reason switch
        {
            PartyLeaveReason.Kicked => "You were removed from the party.",
            PartyLeaveReason.Disbanded => "The party was disbanded.",
            _ => "You left the party.",
        };
        SendLine(characterId, $"{why} Returning to town in {Seconds(grace)}.");
        return true;
    }

    private void OnJoined(uint characterId, Party party)
    {
        if (_countdowns.TryGetValue(characterId, out PartyCountdown? countdown) && countdown.Party.Equals(party.Id))
            _countdowns.Remove(characterId);
    }

    private void AdvanceCountdowns(DateTimeOffset now, List<IWorldConnection> due)
    {
        _scratch.Clear();
        foreach ((uint id, PartyCountdown countdown) in _countdowns)
        {
            // Gone from that instance (a portal, a respawn in town, a transfer): nothing left to do.
            if (OnlineConnection(id) is not { Character: { } character } connection || character.InstanceId != countdown.InstanceId)
            {
                _scratch.Add(id);
                continue;
            }

            double remaining = (countdown.Deadline - now).TotalSeconds;
            if (remaining <= 0)
            {
                _scratch.Add(id);
                due.Add(connection);
                _returning[id] = new PendingReturn(countdown.InstanceId, countdown.Party, countdown.Retries);
                continue;
            }

            int seconds = (int)Math.Ceiling(remaining);
            int announce = 0;
            while (countdown.NextMark < s_countdownMarks.Length && seconds <= s_countdownMarks[countdown.NextMark])
                announce = s_countdownMarks[countdown.NextMark++];

            if (announce > 0)
                SendLine(id, $"Returning to town in {Seconds(announce)}.");
        }

        foreach (uint id in _scratch)
            _countdowns.Remove(id);
    }

    private static string Seconds(int n) => n == 1 ? "1 second" : $"{n} seconds";

    private Party? PartyForInvite(PartyInvite invite)
    {
        if (invite.PartyId is { } id)
            return _parties.GetValueOrDefault(id.Value);

        // The inviter had no party when it invited: it leads the one it has now, or a new one is formed.
        if (PartyOf(invite.InviterId) is { } current)
            return current.IsLeader(invite.InviterId) ? current : null;

        if (OnlineConnection(invite.InviterId)?.Character is not { } inviter)
            return null;

        var party = new Party(new PartyId(++_lastPartyId),
            new PartyMember(new CharacterId(invite.InviterId), inviter.Name, inviter.Class, time.GetUtcNow()));
        _parties[party.Id.Value] = party;
        _partyOf[invite.InviterId] = party;
        if (inviter is CharacterEntity entity)
            entity.PartyId = party.Id;

        logger.LogInformation("Party {PartyId} formed by character {CharacterId}", party.Id, invite.InviterId);
        return party;
    }

    private void Join(Party party, ICharacter character)
    {
        uint id = character.Guid.Id;
        party.Add(new PartyMember(new CharacterId(id), character.Name, character.Class, time.GetUtcNow()));
        _partyOf[id] = party;
        if (character is CharacterEntity entity)
            entity.PartyId = party.Id;

        OnJoined(id, party);
        ResetStatuses(party);
        SendRoster(party);
        SendLine(party, $"{character.Name} joined the party.");
    }

    private void RemoveMember(Party party, uint memberId, PartyLeaveReason reason)
    {
        PartyMember member = party.Find(memberId)!;
        party.Remove(memberId);
        _partyOf.Remove(memberId);
        Detach(memberId);
        OnRemovedFromParty(memberId, party, reason);

        SendLine(party, reason == PartyLeaveReason.Kicked
            ? $"{member.Name} was removed from the party."
            : $"{member.Name} left the party.");

        if (party.Members.Count < 2)
        {
            Disband(party);
            return;
        }

        if (party.Leader.Value == memberId)
            HandOverLeadership(party);

        SendRoster(party);
    }

    private void Disband(Party party)
    {
        _parties.Remove(party.Id.Value);
        _instances.ForgetParty(party.Id);

        foreach (PartyMember member in party.Members.ToArray())
        {
            uint id = member.Id.Value;
            _partyOf.Remove(id);
            Detach(id);
            if (!OnRemovedFromParty(id, party, PartyLeaveReason.Disbanded))
                SendLine(id, "The party was disbanded.");
        }

        logger.LogInformation("Party {PartyId} disbanded", party.Id);
    }

    /// <summary>A character that is no longer a member: no party on the entity, and an empty roster.</summary>
    private void Detach(uint characterId)
    {
        _statusSent.Remove(characterId);
        if (OnlineConnection(characterId) is not { } connection)
            return;

        if (connection.Character is CharacterEntity entity)
            entity.PartyId = null;
        connection.Send(SPartyRosterPacket.Empty(PacketEncoder.Shared));
    }

    /// <summary>To the longest-standing online member other than the current leader; if none is online, the leader stays.</summary>
    private void HandOverLeadership(Party party)
    {
        foreach (PartyMember member in party.Members)
        {
            if (member.Id.Value == party.Leader.Value || !_onlineCharacters.IsOnline(member.Id.Value))
                continue;

            party.Leader = member.Id;
            SendRoster(party);
            SendLine(party, $"{member.Name} is now the party leader.");
            return;
        }

        // Nobody else online. A leader who has left the party itself is replaced by the first member regardless.
        if (party.Find(party.Leader.Value) is null && party.Members.Count > 0)
            party.Leader = party.Members[0].Id;
    }

    private void ExpireInvites(DateTimeOffset now)
    {
        _scratch.Clear();
        foreach ((uint targetId, PartyInvite invite) in _invites)
        {
            if (invite.ExpiresAt <= now)
                _scratch.Add(targetId);
        }

        foreach (uint targetId in _scratch)
        {
            _invites.Remove(targetId, out PartyInvite? invite);
            Tell(invite!.InviterId, PartyResult.InviteExpired, invite.TargetName);
            Tell(targetId, PartyResult.InviteExpired, invite.InviterName);
        }

        // A dropped invite expires for its inviter only: its target never saw it.
        _droppedScratch.Clear();
        foreach (((uint Inviter, uint Target) key, PartyInvite invite) in _droppedInvites)
        {
            if (invite.ExpiresAt <= now)
                _droppedScratch.Add(key);
        }

        foreach ((uint Inviter, uint Target) key in _droppedScratch)
        {
            _droppedInvites.Remove(key, out PartyInvite? invite);
            Tell(key.Inviter, PartyResult.InviteExpired, invite!.TargetName);
        }
    }

    /// <summary>Every invite this character sent or holds ends; the other side is told it expired.</summary>
    private void EndInvitesOf(uint characterId)
    {
        if (_invites.Remove(characterId, out PartyInvite? held))
            Tell(held.InviterId, PartyResult.InviteExpired, held.TargetName);

        _scratch.Clear();
        foreach ((uint targetId, PartyInvite invite) in _invites)
        {
            if (invite.InviterId == characterId)
                _scratch.Add(targetId);
        }

        foreach (uint targetId in _scratch)
        {
            _invites.Remove(targetId, out PartyInvite? sent);
            Tell(targetId, PartyResult.InviteExpired, sent!.InviterName);
        }

        // Dropped invites (#723): one this character held ends for its inviter as a held invite does; one it sent ends
        // silently, since its target never saw it.
        _droppedScratch.Clear();
        foreach ((uint Inviter, uint Target) key in _droppedInvites.Keys)
        {
            if (key.Inviter == characterId || key.Target == characterId)
                _droppedScratch.Add(key);
        }

        foreach ((uint Inviter, uint Target) key in _droppedScratch)
        {
            _droppedInvites.Remove(key, out PartyInvite? dropped);
            if (key.Target == characterId)
                Tell(key.Inviter, PartyResult.InviteExpired, dropped!.TargetName);
        }
    }

    private void Tell(uint characterId, PartyResult result, string? name)
    {
        if (OnlineConnection(characterId) is { } connection)
            connection.Send(SPartyResultPacket.Create(result, name, PacketEncoder.Shared));
    }

    private void SendRoster(Party party)
    {
        DateTimeOffset now = time.GetUtcNow();
        uint lockedForMs = party.ExperienceModeLockedUntil is { } until && until > now
            ? (uint)(until - now).TotalMilliseconds
            : 0u;

        foreach (PartyMember recipient in party.Members)
        {
            if (OnlineConnection(recipient.Id.Value) is not { } connection)
                continue;

            Guid? recipientInstance = connection.Character?.InstanceId;
            var members = new List<PartyMemberDto>(party.Members.Count);
            foreach (PartyMember member in party.Members)
            {
                IWorldConnection? memberConnection = OnlineConnection(member.Id.Value);
                ICharacter? character = memberConnection?.Character;
                if (character is not null)
                    _lastLevel[member.Id.Value] = character.Level;

                members.Add(new PartyMemberDto
                {
                    CharacterId = member.Id.Value,
                    Name = member.Name,
                    Class = (ushort)member.Class,
                    Level = _lastLevel.GetValueOrDefault(member.Id.Value),
                    IsLeader = party.IsLeader(member.Id.Value),
                    Online = character is not null,
                    SameInstance = character is not null && character.InstanceId == recipientInstance,
                });
            }

            connection.Send(SPartyRosterPacket.Create(party.Id.Value, party.ExperienceMode, lockedForMs, members,
                PacketEncoder.Shared));
        }
    }

    private readonly record struct MemberStatus(uint Health, uint MaxHealth, uint Power, uint MaxPower,
        PowerType PowerType, bool IsDead)
    {
        public static MemberStatus Of(ICharacter c) =>
            new(c.CurrentHealth, c.Health, c.CurrentPower ?? 0, c.Power ?? 0, c.PowerType, c.IsDead);
    }

    private sealed record PartyInvite(uint InviterId, string InviterName, string TargetName, PartyId? PartyId,
        DateTimeOffset ExpiresAt);

    private sealed class PartyCountdown(Guid instanceId, PartyId party, DateTimeOffset deadline)
    {
        public Guid InstanceId { get; } = instanceId;
        public PartyId Party { get; } = party;
        public DateTimeOffset Deadline { get; } = deadline;
        public int NextMark { get; set; }

        /// <summary>Returns already tried and failed before this countdown (0 for the countdown a removal starts).</summary>
        public int Retries { get; init; }
    }

    /// <summary>A countdown that ran out and whose return to town is under way (#700).</summary>
    private sealed record PendingReturn(Guid InstanceId, PartyId Party, int Retries);

    private sealed class NoPartyInstances : IPartyInstanceRegistry
    {
        public static readonly NoPartyInstances Instance = new();
        public bool IsPartyInstance(PartyId party, Guid instanceId) => false;
        public void ForgetParty(PartyId party) { }

        public Task<IMapInstance> GetOrCreatePartyInstanceAsync(PartyId party, MapTemplateId templateId) =>
            throw new InvalidOperationException("No instance registry is attached.");
    }
}

/// <summary>Why a character stopped being a member.</summary>
public enum PartyLeaveReason
{
    Left,
    Kicked,
    Disbanded,
}

using System.Runtime.InteropServices;
using Avalon.Common.Mathematics;
using Avalon.Network.Packets.Party;
using Avalon.Network.Packets.Serialization;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;

namespace Avalon.World.Parties;

/// <summary>
/// Where each party member's far party members stand, for the client map (#1199, owner decisions): sent to each member
/// about its party members in its own instance that its world-state view does not hold, about once a second
/// (<see cref="PartyService.PositionsInterval" />), and at once when its view lets go of one. Owned by
/// <see cref="PartyService" />, which tells it of every change of who shares an instance or a party; tick thread only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who is listed</b> is the replication state, never a distance of its own: a member is in a viewer's list exactly
/// while the viewer's <see cref="CharacterCharacterGameState.Knows(Avalon.Common.ObjectGuid)" /> says its client was not told of it, as of the
/// view's last decision. The flush runs after the world update, so a member leaves the list on the tick its world-state
/// add is sent and joins it on the tick its remove is sent; the remove makes the viewer due at once
/// (<see cref="LeftView" />), so the client never goes a tick without either. A viewer whose view is owed a decision
/// (<see cref="CharacterCharacterGameState.ScanOwed" />: it has just entered an instance, or started over) waits for it.
/// </para>
/// <para>
/// <b>What is visited.</b> Only the watched parties: a party is watched from a change that can leave a member out of
/// another's view (a join, a member's instance change or coming online, a view letting go of a member) until a flush
/// finds every online member quiet, its last list empty and nothing due. A tick with no watched party reads one count,
/// and a watched party with nothing due one compare (<see cref="Party.PositionsDueAt" />: the earliest of its viewers'
/// next turns, set to now by anything that makes one due). Nothing is sent but on a viewer's turn, and every turn checks
/// membership and instance afresh; a leave, a kick, a disband, a logout or an instance change makes the members left
/// due at once (<see cref="Watch" />), so they are sent their lists without the one who went on the tick it happens.
/// </para>
/// </remarks>
internal sealed class PartyPositions(TimeProvider time, Func<uint, Party?> partyOf, Func<uint, IWorldConnection?> online)
{
    private readonly List<Party> _watched = [];
    private readonly Dictionary<uint, Viewer> _viewers = [];

    // One viewer's list at a time, from entries that only grow, encoded before the next viewer's is filled.
    private readonly List<PartyMemberPositionDto> _list = [];
    private readonly List<PartyMemberPositionDto> _entries = [];

    /// <summary>Who shares an instance or the party changed: every member is listed afresh on the next flush.</summary>
    public void Watch(Party party)
    {
        IReadOnlyList<PartyMember> members = party.Members;
        for (int i = 0; i < members.Count; i++)
        {
            uint id = members[i].Id.Value;
            if (online(id) is not null)
                CollectionsMarshal.GetValueRefOrAddDefault(_viewers, id, out _).Due = true;
        }

        party.PositionsDueAt = long.MinValue;
        Track(party);
    }

    /// <summary>The viewer's world-state view let go of a member of its party: listed at once, on this tick.</summary>
    public void LeftView(uint viewerId, Party party)
    {
        CollectionsMarshal.GetValueRefOrAddDefault(_viewers, viewerId, out _).Due = true;
        party.PositionsDueAt = long.MinValue;
        Track(party);
    }

    /// <summary>The character left the party or the world: nothing more is sent to it, and its memory is dropped.</summary>
    public void Forget(uint characterId) => _viewers.Remove(characterId);

    /// <summary>
    /// Once per tick, after the world update: each watched party's online members due (a list owed at once, or one
    /// <see cref="PartyService.PositionsInterval" /> after the last) are each sent the members their view does not hold;
    /// a party found quiet is no longer watched.
    /// </summary>
    public void Flush()
    {
        if (_watched.Count == 0)
            return;

        long now = time.GetUtcNow().UtcTicks;
        for (int p = _watched.Count - 1; p >= 0; p--)
        {
            Party party = _watched[p];
            if (FlushParty(party, now))
                continue;

            party.PositionsWatched = false;
            int last = _watched.Count - 1;
            _watched[p] = _watched[last];
            _watched.RemoveAt(last);
        }
    }

    /// <returns>Whether the party is still watched.</returns>
    private bool FlushParty(Party party, long now)
    {
        // Nobody's turn yet: the one compare a watched party costs on most ticks.
        if (now < party.PositionsDueAt)
            return true;

        bool watched = false;
        long dueAt = long.MaxValue;
        IReadOnlyList<PartyMember> members = party.Members;
        for (int v = 0; v < members.Count; v++)
        {
            uint viewerId = members[v].Id.Value;
            // A disbanded party keeps its members' list, but none of them is in it any more.
            if (!ReferenceEquals(partyOf(viewerId), party)
                || online(viewerId) is not { Character: CharacterEntity viewer } connection)
            {
                continue;
            }

            ref Viewer state = ref CollectionsMarshal.GetValueRefOrAddDefault(_viewers, viewerId, out _);
            if (viewer.CharacterGameState.ScanOwed)
            {
                // Its view is decided on its instance's next tick, before the next flush: listed then.
                state.Due = true;
                watched = true;
                dueAt = long.MinValue;
                continue;
            }

            if (!state.Due && now < state.NextAt)
            {
                if (state.Holding)
                {
                    watched = true;
                    dueAt = Math.Min(dueAt, state.NextAt);
                }

                continue;
            }

            Fill(party, viewer);
            if (_list.Count > 0 || state.Holding)
            {
                connection.Send(SPartyPositionsPacket.Create(_list, PacketEncoder.Shared));
                state.NextAt = now + PartyService.PositionsInterval.Ticks;
            }

            state.Holding = _list.Count > 0;
            state.Due = false;
            if (state.Holding)
            {
                watched = true;
                dueAt = Math.Min(dueAt, state.NextAt);
            }
        }

        // A viewer that is not holding has no turn of its own: only a change that makes it due (Watch, LeftView) gives it
        // one, and that sets the party's due time back to now.
        party.PositionsDueAt = dueAt;
        return watched;
    }

    /// <summary>The viewer's list: each other online member in its instance its view does not hold, where it stands.</summary>
    private void Fill(Party party, CharacterEntity viewer)
    {
        _list.Clear();
        Guid instance = viewer.InstanceId;
        if (instance == Guid.Empty)
            return;

        CharacterCharacterGameState view = viewer.CharacterGameState;
        IReadOnlyList<PartyMember> members = party.Members;
        for (int i = 0; i < members.Count; i++)
        {
            uint memberId = members[i].Id.Value;
            if (memberId == viewer.Guid.Id || online(memberId)?.Character is not { } member
                || member.InstanceId != instance || view.Knows(member.Guid))
            {
                continue;
            }

            // A position that is not finite is in no view and on no map.
            Vector3 position = member.Position;
            if (!EffectAudience.IsFinite(position))
                continue;

            if (_list.Count == _entries.Count)
                _entries.Add(new PartyMemberPositionDto());

            PartyMemberPositionDto entry = _entries[_list.Count];
            entry.CharacterId = memberId;
            entry.X = position.x;
            entry.Z = position.z;
            _list.Add(entry);
        }
    }

    private void Track(Party party)
    {
        if (party.PositionsWatched)
            return;

        party.PositionsWatched = true;
        _watched.Add(party);
    }

    /// <summary>What one viewer was last sent, and when it is next due.</summary>
    private struct Viewer
    {
        /// <summary>A list is owed on the next flush, whatever the interval.</summary>
        public bool Due;

        /// <summary>The last list sent listed someone, so an empty one follows when nobody is left.</summary>
        public bool Holding;

        /// <summary>When the next list is due (UTC ticks), one interval after the last.</summary>
        public long NextAt;
    }
}

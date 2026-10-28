using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Party;

namespace Avalon.World.Parties;

/// <summary>
/// A party (spec 2026-09-30 section 1). Only <see cref="PartyService" /> changes it, on the tick. Members are
/// in join order, so the first is the longest-standing.
/// </summary>
public sealed class Party
{
    private readonly List<PartyMember> _members = [];

    internal Party(PartyId id, PartyMember leader)
    {
        Id = id;
        Leader = leader.Id;
        _members.Add(leader);
    }

    public PartyId Id { get; }

    public CharacterId Leader { get; internal set; }

    public IReadOnlyList<PartyMember> Members => _members;

    public PartyExperienceMode ExperienceMode { get; private set; } = PartyExperienceMode.Even;

    /// <summary>Until when the leader may not switch the mode again; null when never switched.</summary>
    public DateTimeOffset? ExperienceModeLockedUntil { get; private set; }

    /// <summary>Whether <see cref="PartyPositions" /> visits it on its flush (#1199).</summary>
    internal bool PositionsWatched { get; set; }

    /// <summary>
    /// When <see cref="PartyPositions" /> next has a viewer of it to visit (UTC ticks): the earliest of its viewers' next
    /// turns, and <see cref="long.MinValue" /> once a change makes one due, so a watched party with nobody due costs one
    /// compare a tick (#1199).
    /// </summary>
    internal long PositionsDueAt { get; set; }

    public bool IsLeader(uint characterId) => Leader.Value == characterId;

    public PartyMember? Find(uint characterId)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (_members[i].Id.Value == characterId)
                return _members[i];
        }

        return null;
    }

    public PartyMember? FindByName(string name)
    {
        for (int i = 0; i < _members.Count; i++)
        {
            if (CharacterName.Same(_members[i].Name, name))
                return _members[i];
        }

        return null;
    }

    internal void Add(PartyMember member) => _members.Add(member);

    internal bool Remove(uint characterId)
    {
        // By index, from the end: RemoveAll over a lambda capturing the id cost a closure and a delegate a call (#973).
        bool removed = false;
        for (int i = _members.Count - 1; i >= 0; i--)
        {
            if (_members[i].Id.Value == characterId)
            {
                _members.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }

    internal void SetExperienceMode(PartyExperienceMode mode, DateTimeOffset lockedUntil)
    {
        ExperienceMode = mode;
        ExperienceModeLockedUntil = lockedUntil;
    }
}

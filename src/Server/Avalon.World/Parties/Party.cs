using Avalon.Common.ValueObjects;
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
            if (string.Equals(_members[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return _members[i];
        }

        return null;
    }

    internal void Add(PartyMember member) => _members.Add(member);

    internal bool Remove(uint characterId) => _members.RemoveAll(m => m.Id.Value == characterId) > 0;

    internal void SetExperienceMode(PartyExperienceMode mode, DateTimeOffset lockedUntil)
    {
        ExperienceMode = mode;
        ExperienceModeLockedUntil = lockedUntil;
    }
}

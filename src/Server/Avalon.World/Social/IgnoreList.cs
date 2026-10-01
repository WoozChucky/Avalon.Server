using Avalon.Database.Character.Repositories;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.World.Persistence;

namespace Avalon.World.Social;

/// <summary>
/// One character's ignore list (#723): the characters whose chat it does not hear and whose party invites it never
/// sees. World-side and tick-thread only, like the inventory and the quest log, and never on ICharacter, so no mod can
/// read or change it. Memory is authoritative: every check runs against it, with no database query per message.
/// Every change marks the save (<see cref="SaveStateTracker.IgnoreChanged" />); <see cref="Load" /> does not. It does
/// not enforce the cap or refuse the owner's own id: the ignore commands decide what may be added.
/// </summary>
public sealed class IgnoreList(SaveStateTracker save)
{
    private readonly List<IgnoredCharacter> _entries = [];
    private readonly HashSet<uint> _ids = [];

    /// <summary>Oldest entry first.</summary>
    public IReadOnlyList<IgnoredCharacter> Entries => _entries;

    public int Count => _entries.Count;

    public bool Contains(uint characterId) => _ids.Contains(characterId);

    /// <summary>The entry with this name, ignoring case and surrounding spaces, or none.</summary>
    public IgnoredCharacter? FindByName(string name)
    {
        string trimmed = name.Trim();
        return _entries.Find(e => string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Replaces the whole list with the stored rows, in their order. Marks nothing.</summary>
    public void Load(IEnumerable<IgnoredCharacterRow> rows)
    {
        _entries.Clear();
        _ids.Clear();
        foreach (IgnoredCharacterRow row in rows)
        {
            if (_ids.Add(row.Id.Value))
                _entries.Add(new IgnoredCharacter(row.Id.Value, row.Name, row.CreatedAt));
        }
    }

    /// <summary>Adds the character at the end of the list; false (and nothing marked) when it is already on it.</summary>
    public bool Add(uint characterId, string name, DateTime createdAt)
    {
        if (!_ids.Add(characterId))
            return false;

        _entries.Add(new IgnoredCharacter(characterId, name, createdAt));
        save.IgnoreChanged(characterId);
        return true;
    }

    /// <summary>Takes the character off the list; false (and nothing marked) when it was not on it.</summary>
    public bool Remove(uint characterId)
    {
        if (!_ids.Remove(characterId))
            return false;

        _entries.RemoveAll(e => e.Id == characterId);
        save.IgnoreChanged(characterId);
        return true;
    }

    /// <summary>SMSG_IGNORE_LIST: the whole list, oldest first.</summary>
    public NetworkPacket ToPacket(EncryptFunc encrypt) =>
        SIgnoreListPacket.Create(
            _entries.Select(e => new IgnoredCharacterDto { CharacterId = e.Id, Name = e.Name }).ToList(), encrypt);
}

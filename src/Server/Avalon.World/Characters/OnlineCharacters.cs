using Avalon.World.Public;
using Avalon.World.Public.Characters;

namespace Avalon.World.Characters;

/// <summary>
/// Every character in the world on this server, by id and by name (ignoring case), with the connection that holds it.
/// A DI singleton, World-side (never on the modding API), tick thread only. It is fed by the world's online and offline
/// hooks, which reach it through <see cref="Parties.PartyService.CharacterOnline" /> and
/// <see cref="Parties.PartyService.CharacterOffline" /> (World.SpawnInInstance and World.LeaveWorldAsync), so the party
/// invite and the whisper (#717) look a name up in one place and cannot disagree about who is online.
/// </summary>
public sealed class OnlineCharacters
{
    private readonly Dictionary<uint, IWorldConnection> _byId = [];
    private readonly Dictionary<string, uint> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The connection holding this online character, or none.</summary>
    public IWorldConnection? ById(uint characterId) => _byId.GetValueOrDefault(characterId);

    /// <summary>The connection of every online character (#723: a deleted character leaves their ignore lists).</summary>
    public IEnumerable<IWorldConnection> Connections => _byId.Values;

    /// <summary>Whether this character is online.</summary>
    public bool IsOnline(uint characterId) => _byId.ContainsKey(characterId);

    /// <summary>The connection holding the online character with this name, ignoring case and surrounding spaces, or none.</summary>
    public IWorldConnection? ByName(string name) => TryIdByName(name, out uint id) ? ById(id) : null;

    /// <summary>The id of the online character with this name, ignoring case and surrounding spaces.</summary>
    public bool TryIdByName(string name, out uint characterId) => _byName.TryGetValue(name.Trim(), out characterId);

    /// <summary>The character the connection holds is online. A connection with no character changes nothing.</summary>
    public void Add(IWorldConnection connection)
    {
        if (connection.Character is not { } character)
            return;

        uint id = character.Guid.Id;
        _byId[id] = connection;
        _byName[character.Name] = id;
    }

    /// <summary>
    /// The character left the world. Only the connection it is online through can take it offline: a stale
    /// connection's leave changes nothing and answers false.
    /// </summary>
    public bool Remove(IWorldConnection connection, ICharacter character)
    {
        uint id = character.Guid.Id;
        if (!_byId.TryGetValue(id, out IWorldConnection? held) || !ReferenceEquals(held, connection))
            return false;

        _byId.Remove(id);
        if (_byName.TryGetValue(character.Name, out uint named) && named == id)
            _byName.Remove(character.Name);
        return true;
    }
}

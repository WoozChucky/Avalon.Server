using Avalon.Domain.Characters;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Threading;

namespace Avalon.World.Characters;

/// <summary>
/// Every character in the world on this server, by id and by name (compared as its key, CharacterName.Key, so in any
/// case, #757), with the connection that holds it. A name is looked up in place, by a slice of what was typed: no key
/// is made (#973).
/// A DI singleton, World-side (never on the modding API), tick thread only. It is fed by the world's online and offline
/// hooks, which reach it through <see cref="Parties.PartyService.CharacterOnline" /> and
/// <see cref="Parties.PartyService.CharacterOffline" /> (World.SpawnInInstance and World.LeaveWorldAsync), so the party
/// invite and the whisper (#717) look a name up in one place and cannot disagree about who is online.
/// Its writers assert the tick thread while <see cref="TickThreadGuard" /> is enabled (#639).
/// </summary>
public sealed class OnlineCharacters(TickThreadGuard? tickThread = null)
{
    private readonly Dictionary<uint, IWorldConnection> _byId = [];
    // The names as the characters hold them, compared as their keys (CharacterName.Key, #757).
    private readonly Dictionary<string, uint> _byName = new(CharacterName.KeyComparer.Instance);

    /// <summary>The connection holding this online character, or none.</summary>
    public IWorldConnection? ById(uint characterId) => _byId.GetValueOrDefault(characterId);

    /// <summary>Whether this character is online.</summary>
    public bool IsOnline(uint characterId) => _byId.ContainsKey(characterId);

    /// <summary>The connection holding the online character with this name, ignoring case and surrounding spaces, or none.</summary>
    public IWorldConnection? ByName(ReadOnlySpan<char> name) => TryIdByName(name, out uint id) ? ById(id) : null;

    /// <summary>The id of the online character with this name, ignoring case and surrounding spaces.</summary>
    public bool TryIdByName(ReadOnlySpan<char> name, out uint characterId) =>
        Names.TryGetValue(name.Trim(), out characterId);

    private Dictionary<string, uint>.AlternateLookup<ReadOnlySpan<char>> Names =>
        _byName.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>The character the connection holds is online. A connection with no character changes nothing.</summary>
    public void Add(IWorldConnection connection)
    {
        tickThread?.AssertOnTick("OnlineCharacters.Add");

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
        tickThread?.AssertOnTick("OnlineCharacters.Remove");

        uint id = character.Guid.Id;
        if (!_byId.TryGetValue(id, out IWorldConnection? held) || !ReferenceEquals(held, connection))
            return false;

        _byId.Remove(id);
        if (Names.TryGetValue(character.Name, out uint named) && named == id)
            Names.Remove(character.Name);
        return true;
    }
}

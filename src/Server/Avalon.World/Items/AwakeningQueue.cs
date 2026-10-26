using System.Runtime.InteropServices;
using Avalon.World.Entities;
using Avalon.World.Public;

namespace Avalon.World.Items;

/// <summary>
/// The characters whose Awakening meter has work for the tick's flush (#1180): one per world, a DI singleton shared by
/// World, which attaches a character as it enters an instance and detaches it as it leaves the world, and WorldServer,
/// whose Awakening stage takes what was queued since the last tick and flushes each. A meter queues its character once
/// each time it gets work (a kill, a settle that moved progress, a recount that found a seal), so the stage visits only
/// those characters: an idle tick costs nothing per connection. Two reused lists, so the stage walks one while a flush
/// queues work for the next tick on the other; nothing is allocated once they have grown. Tick thread only.
/// </summary>
public sealed class AwakeningQueue
{
    private List<CharacterEntity> _queued = [];
    private List<CharacterEntity> _taken = [];

    /// <summary>
    /// <paramref name="character" /> is in the world through <paramref name="connection" />: its meter queues it here
    /// from now on whenever it has work, and at once when it already has.
    /// </summary>
    public void Attach(CharacterEntity character, IWorldConnection connection) =>
        character.Awakening.Attach(this, character, connection);

    /// <summary><paramref name="character" /> left the world: it is queued no more, and an entry already queued is passed over.</summary>
    public static void Detach(CharacterEntity character) => character.Awakening.Detach();

    /// <summary>
    /// The characters queued since the last take, each once, in the order they were queued; valid until the next take.
    /// What a flush queues meanwhile waits for the next.
    /// </summary>
    public ReadOnlySpan<CharacterEntity> Take()
    {
        (_queued, _taken) = (_taken, _queued);
        _queued.Clear();
        return CollectionsMarshal.AsSpan(_taken);
    }

    internal void Add(CharacterEntity character) => _queued.Add(character);
}

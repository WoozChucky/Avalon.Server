using Avalon.Common.Mathematics;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;

namespace Avalon.World.Doodads;

/// <summary>
/// What a doodad use needs from the instance its user is in. MapInstance implements it; World-side, not the modding API.
/// Tick thread only.
/// </summary>
public interface IDoodadHost : IMapInstance, IAuraHost
{
    /// <summary>The doodads placed here.</summary>
    DoodadIndex Doodads { get; }

    /// <summary>The doodad cast bars here, at most one per character.</summary>
    DoodadCasts DoodadCasts { get; }

    /// <summary>The item cast bars here: a doodad use that passed its checks ends the user's.</summary>
    ItemUseCasts ItemUses { get; }

    /// <summary>The spent doodads waiting to be ready again, soonest first.</summary>
    DoodadRespawns Respawns { get; }

    /// <summary>The container's clock, the one the instance ticks its casts and respawns on.</summary>
    TimeProvider Clock { get; }

    /// <summary>The doodad's state changed: every client that sees it is owed an update at the next broadcast.</summary>
    void DoodadChanged(Doodad doodad);

    /// <summary>
    /// Rolls <paramref name="doodad" />'s loot table and copper range and drops them on the ring around it, as a kill
    /// drops: allocated by the instance's loot allocator among <paramref name="user" /> and the members of its party in
    /// this instance, stored, and told to every connection here in one shared loot packet. Contained: a failure is
    /// logged and drops what was stored before it. How many drops it stored.
    /// </summary>
    int DropLootAt(Doodad doodad, CharacterEntity user);

    /// <summary>
    /// Adds the living players here within <paramref name="radius" /> of <paramref name="centre" /> on X/Z to
    /// <paramref name="into" />, an empty list the caller keeps; never a creature. A distance that is not a number is
    /// out of reach, and a negative radius or one that is not a finite number reaches no one.
    /// </summary>
    void PlayersWithin(Vector3 centre, float radius, List<CharacterEntity> into);

    /// <summary>
    /// Heals <paramref name="target" /> by <paramref name="amount" /> through the instance's combat service, capped at its
    /// maximum, the dead never; what it restored.
    /// </summary>
    uint RestoreHealth(IUnit healer, IUnit target, uint amount);
}

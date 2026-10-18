using Avalon.World.Auras;
using Avalon.World.Items;
using Avalon.World.Public.Instances;

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
}

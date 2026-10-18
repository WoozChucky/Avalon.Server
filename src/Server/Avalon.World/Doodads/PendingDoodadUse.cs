using Avalon.Common.Mathematics;
using Avalon.World.Entities;
using Avalon.World.Public;

namespace Avalon.World.Doodads;

/// <summary>One character's doodad cast in progress (DoodadCasts), at most one per character.</summary>
/// <param name="Character">Who is using it.</param>
/// <param name="Doodad">What it is using.</param>
/// <param name="StartPosition">Where it stood when it began; any other position interrupts, as for an item.</param>
/// <param name="CastId">From the instance's cast ids, the ones its ability and item casts take, so a client keys all alike.</param>
/// <param name="EndsAt">When it completes, on the container's clock: absolute, so nothing counts it down.</param>
/// <param name="CastTimeSeconds">Its length, as its start packet tells clients.</param>
/// <param name="Connection">The user's connection, for the script's lines to the user when it completes.</param>
public readonly record struct PendingDoodadUse(
    CharacterEntity Character,
    Doodad Doodad,
    Vector3 StartPosition,
    uint CastId,
    DateTime EndsAt,
    float CastTimeSeconds,
    IWorldConnection Connection);

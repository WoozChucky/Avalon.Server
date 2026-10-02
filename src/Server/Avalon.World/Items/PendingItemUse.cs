using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World.Entities;

namespace Avalon.World.Items;

/// <summary>One character's item cast in progress.</summary>
public sealed class PendingItemUse
{
    public required CharacterEntity Character { get; init; }
    public required ItemTemplateId Item { get; init; }

    /// <summary>Where the character stood when it began; any other position interrupts, as for an ability.</summary>
    public required Vector3 StartPosition { get; init; }

    /// <summary>From the instance's cast ids, the ones its ability casts take, so a client keys both alike.</summary>
    public required uint CastId { get; init; }

    public required float CastTimeSeconds { get; init; }
    public float Elapsed { get; set; }

    /// <summary>Asked when the time has run: false interrupts instead (the item left its slot).</summary>
    public required Func<bool> CanComplete { get; init; }

    public required Action Completed { get; init; }
    public required Action Interrupted { get; init; }
}

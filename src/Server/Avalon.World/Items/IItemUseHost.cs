using Avalon.World.Abilities.Targeting;
using Avalon.World.Auras;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;

namespace Avalon.World.Items;

/// <summary>What an item use needs from the instance its user is in. MapInstance implements it; World-side, not the modding API.</summary>
public interface IItemUseHost : IMapInstance, IAuraHost
{
    ItemUseCasts ItemUses { get; }

    /// <summary>An item's heal through this instance's combat service (CombatService.RestoreHealth).</summary>
    uint RestoreHealth(IUnit healer, IUnit target, uint amount);

    /// <summary>The instance's living units by shape, for an item's damage targets.</summary>
    IHitQuery Hits { get; }

    /// <summary>
    /// An ability an item's script casts (item use), through the cast system like any cast: queued when it has a cast
    /// time, instant otherwise; <paramref name="free" /> skips the cost only.
    /// </summary>
    bool CastForItem(IUnit caster, AbilityAim aim, IAbility ability, bool free);

    /// <summary>
    /// A summoned creature (item use) leaves the instance once <paramref name="lifetime" /> has passed on the
    /// instance's clock, unless it is killed first (then the corpse removal has it). Checked on the instance's tick;
    /// an instance nobody is in does not tick, so its summons leave on its next occupied tick.
    /// </summary>
    void DespawnAfter(ICreature creature, TimeSpan lifetime);
}

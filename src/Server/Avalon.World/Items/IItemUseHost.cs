using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;

namespace Avalon.World.Items;

/// <summary>What an item use needs from the instance its user is in. MapInstance implements it; World-side, not the modding API.</summary>
public interface IItemUseHost : IMapInstance
{
    ItemUseCasts ItemUses { get; }

    /// <summary>An item's heal through this instance's combat service (CombatService.RestoreHealth).</summary>
    uint RestoreHealth(IUnit healer, IUnit target, uint amount);
}

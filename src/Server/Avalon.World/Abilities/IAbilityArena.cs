using Avalon.Common.Mathematics;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;

namespace Avalon.World.Abilities;

/// <summary>
/// What a shape script may use (#164). World-side on purpose: it is not part of the modding API.
/// <c>MapInstance</c> implements it.
/// </summary>
public interface IAbilityArena
{
    /// <summary>A town never allows player hostility.</summary>
    MapType MapType { get; }

    ICombatService CombatService { get; }

    IMapNavigator GetNavigatorForPosition(Vector3 position);
}

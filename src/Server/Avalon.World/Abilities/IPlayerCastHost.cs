using Avalon.World.Public.Abilities;
using Avalon.World.Public.Characters;

namespace Avalon.World.Abilities;

/// <summary>
/// A player's instant cast, judged by that player's screen (#1082). The cast handler fires through it when the caster's
/// instance is one (<c>MapInstance</c>, explicitly), and through <c>ISimulationContext.RunInstantAbility</c> otherwise.
/// Internal and World-side: nothing in the modding API may name a tick to judge hits at.
/// </summary>
internal interface IPlayerCastHost
{
    /// <summary>
    /// As <c>ISimulationContext.RunInstantAbility</c>, with the cast's hits rewound to <paramref name="viewTick" />, the
    /// world tick the client was drawing other units at (<c>CCastAbilityPacket.ViewTick</c>, 0 when not sent), clamped to
    /// <see cref="LagCompensation.MaxRewindTicks" /> behind the present.
    /// </summary>
    bool RunInstantAbility(ICharacter caster, AbilityAim aim, IAbility ability, long viewTick);
}

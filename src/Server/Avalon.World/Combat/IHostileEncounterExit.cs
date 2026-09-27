using Avalon.World.Public.Units;

namespace Avalon.World.Combat;

/// <summary>
/// Takes a creature out of its encounter with its threat list (#614). A creature home again after the
/// leash does this, so nothing from before steers its next fight. <see cref="CombatService" />
/// implements it.
/// </summary>
/// <remarks>
/// Internal and off <c>ICombatService</c> on purpose: every AI script reaches the combat service, and a
/// mod calling this every tick could keep any creature from ever holding threat.
/// </remarks>
internal interface IHostileEncounterExit
{
    /// <summary>
    /// Removes <paramref name="hostile" /> and its threat list from its encounter. The players stay,
    /// with their threat on every other hostile. Idempotent.
    /// </summary>
    void DropHostileFromEncounter(IUnit hostile);
}

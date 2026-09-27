namespace Avalon.World.Public.Abilities;

/// <summary>
/// Which of the caster's derived damage stats an ability scales with (#506): AttackDamage for a weapon
/// skill, AbilityDamage for a spell. Stored as a byte; append-only.
/// </summary>
public enum ScalingStat : byte
{
    Attack = 0,
    Ability = 1,
}

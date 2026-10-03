namespace Avalon.Domain.World;

/// <summary>
/// What applying an aura the unit already holds does: Refresh renews it; Stack adds one stack up to MaxStacks and renews
/// it; Independent keeps one copy per caster.
/// </summary>
public enum AuraStacking : byte { Refresh = 1, Stack = 2, Independent = 3 }

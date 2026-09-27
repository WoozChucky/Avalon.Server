namespace Avalon.World.Scripts.Creatures;

/// <summary>
/// Whether a creature's AI has it walking home, after the leash or a lost target (#610). A creature
/// walking home ignores hits, and <c>CombatService</c> asks this to refuse them before any encounter,
/// threat or combat tag. <see cref="CreatureCombatScript" /> answers it, and every script that chains
/// one forwards it.
/// </summary>
/// <remarks>
/// Internal and read-only on purpose: no mod or script can declare it directly, so none can make a
/// creature unhittable by claiming it is walking home. A subclass of <see cref="CreatureCombatScript" />
/// inherits it, with its behaviour.
/// </remarks>
internal interface IReturningHome
{
    bool IsReturningHome { get; }
}

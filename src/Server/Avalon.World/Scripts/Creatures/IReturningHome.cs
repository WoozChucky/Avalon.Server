namespace Avalon.World.Scripts.Creatures;

/// <summary>
/// Whether a creature's AI has it walking home, after the leash or a lost target (#610). A creature
/// walking home ignores hits, and <c>CombatService</c> asks this to refuse them before any encounter,
/// threat or combat tag. <see cref="CreatureCombatScript" /> answers it, and every script that chains
/// one forwards it.
/// </summary>
/// <remarks>
/// Internal and read-only on purpose: a script on the modding API (World.Public, or a hot-reloaded
/// assembly) cannot implement it, so no mod can make a creature unhittable by claiming it is walking
/// home.
/// </remarks>
internal interface IReturningHome
{
    bool IsReturningHome { get; }
}

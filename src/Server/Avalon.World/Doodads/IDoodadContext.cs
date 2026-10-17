using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;

namespace Avalon.World.Doodads;

/// <summary>
/// What a doodad script's hook may read of its doodad and do to it. One reusable instance per caller, valid during the
/// call alone: any member used after the hook returned throws <see cref="InvalidOperationException" />. World-side, not
/// the modding API. The acts that change the world (<see cref="Despawn" />, <see cref="RespawnAfter" />) belong to the
/// tick hooks; in <see cref="DoodadScript.OnSpawn" />, off the tick, they do nothing.
/// </summary>
public interface IDoodadContext
{
    DoodadTemplateId TemplateId { get; }

    string Name { get; }

    Vector3 Position { get; }

    DoodadState State { get; }

    /// <summary>How far the template's effects reach, in metres.</summary>
    float AreaRadius { get; }

    /// <summary>The share of health and power the template restores, in percent.</summary>
    uint RestorePct { get; }

    /// <summary>The auras a use may apply, each with its weight out of <see cref="OutcomeWeightTotal" />.</summary>
    IReadOnlyList<DoodadOutcomeView> AuraOutcomes { get; }

    uint OutcomeWeightTotal { get; }

    /// <summary>Ready or Used; a doodad leaves views through <see cref="Despawn" />, never through this.</summary>
    void SetState(DoodadState state);

    /// <summary>Removes the doodad from every view. Tick hooks only; refused for a solid doodad, which never leaves.</summary>
    void Despawn();

    /// <summary>Asks for the doodad to be made ready again after <paramref name="delay" />, above zero. Tick hooks only.</summary>
    void RespawnAfter(TimeSpan delay);

    /// <summary>A number in [0, <paramref name="maxExclusive" />), from the instance's loot randomness.</summary>
    long NextRandom(long maxExclusive);

    /// <summary>
    /// Tells every viewer that a rule only the script knows changed: each asks <see cref="DoodadScript.CanSee" /> again at
    /// its next view decision, and none before. Tick hooks only; at spawn, nothing has been decided yet.
    /// </summary>
    void RefreshVisibility();
}

/// <summary>
/// <see cref="IDoodadContext" /> for a use: the user, and the acts a use may take on the players around the doodad.
/// </summary>
public interface IDoodadUseContext : IDoodadContext
{
    string UserName { get; }

    bool UserInParty { get; }

    /// <summary>Drops the template's loot table and gold at the doodad, for the user and its party; how many drops.</summary>
    int DropLoot();

    /// <summary>Applies <paramref name="aura" /> to every living player within <paramref name="radius" /> on X/Z; how many.</summary>
    int ApplyAuraInRadius(AuraId aura, float radius);

    /// <summary>Restores those shares of health and power to every living player within <paramref name="radius" />; how many.</summary>
    int RestoreInRadius(uint healthPct, uint powerPct, float radius);

    /// <summary>A system line to the user.</summary>
    void Tell(string line);
}

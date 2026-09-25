using System;
using Avalon.Common.Mathematics;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;

namespace Avalon.World.Public.Creatures;

public delegate void CreatureKilledDelegate(ICreature creature, IUnit killer);

public delegate void UnitAttackAnimationDelegate(IUnit unit, IAbility? spell);

public delegate void UnitFinishedCastAnimationDelegate(IUnit unit, IAbility spell);

public delegate void UnitInterruptedCastAnimationDelegate(IUnit unit, IAbility spell);

public delegate void UnitDamagedDelegate(IUnit unit, IUnit attacker, uint damage);

public interface ICreature : IUnit
{
    ICreatureMetadata Metadata { get; set; }

    /// <summary>Lower bound of this creature's melee damage, derived at spawn.</summary>
    uint DamageMin { get; set; }

    /// <summary>Upper bound of this creature's melee damage, derived at spawn.</summary>
    uint DamageMax { get; set; }

    /// <summary>
    /// Experience this kill awards, derived at spawn and before any map-band scaling. Distinct from
    /// <see cref="ICreatureMetadata.Experience" />, which is the template's optional override.
    /// </summary>
    uint Experience { get; set; }
    /// <summary>
    /// When true this creature can never be damaged. ICombatService drops the hit before it reaches
    /// OnHit, so no health is lost, no threat accrues and no encounter is created. Town NPCs set it;
    /// monsters do not. Copied from the template at spawn, never changed at runtime.
    /// </summary>
    bool Invulnerable { get; set; }

    string Name { get; set; }
    float Speed { get; set; }
    string ScriptName { get; set; }
    AiScript? Script { get; set; }

    /// <summary>
    /// The points this creature walks, in order, looping, in world coordinates. Empty for a creature
    /// with no path, which is every creature today unless its authored spawn names one. Per creature,
    /// not on <see cref="Metadata"/>: two spawns of the same template can walk different routes.
    /// </summary>
    IReadOnlyList<PatrolPoint> PatrolPath { get; set; }

    IUnit?   TauntedBy      { get; set; }
    DateTime TauntExpiresAt { get; set; }

    void LookAt(Vector3 target);
    bool IsLookingAt(Vector3 target, float threshold = 0.1f);
    void Died(IUnit killer);
}

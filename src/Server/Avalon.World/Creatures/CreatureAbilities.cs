using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities;
using Avalon.World.Public.Abilities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Creatures;

/// <summary>
/// The abilities a creature script declares (#163): its <see cref="Basic" />, which it attacks with every
/// swing interval in place of the old raw swing, and its specials, in the order the script prefers them.
/// The ids are code; the numbers behind them are the ability rows.
/// </summary>
public sealed record CreatureAbilityKit(AbilityId Basic, params AbilityId[] Specials);

/// <summary>
/// A creature's abilities (#163): its own clones of the ability rows its script declares, their cooldowns,
/// and whether one is being cast. Owned by the World-side <c>Creature</c> and never exposed on ICreature or
/// World.Public (#622). Tick thread only.
/// </summary>
/// <remarks>
/// The cast system reads and writes the clones as it does a character's: it marks one <c>Casting</c> while
/// its cast time runs and sets its cooldown when it fires. The basic's cooldown is the creature's
/// SwingInterval, which already carries its haste; every other one is its row's cooldown divided by the
/// creature's haste. Casts are free: a creature has no pool.
/// </remarks>
public sealed class CreatureAbilities
{
    private IAbility[] _abilities = [];

    /// <summary>Every ability loaded, the basic first, then the specials in the kit's order.</summary>
    public IReadOnlyList<IAbility> All => _abilities;

    /// <summary>The basic, or null when the kit's basic is not in the catalog.</summary>
    public IAbility? Basic { get; private set; }

    /// <summary>Whether a cast-time ability is being cast, from the cast's start until it fires or ends.</summary>
    public bool IsCasting
    {
        get
        {
            foreach (IAbility ability in _abilities)
            {
                if (ability.Casting)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public IAbility? this[AbilityId id]
    {
        get
        {
            foreach (IAbility ability in _abilities)
            {
                if (ability.AbilityId == id)
                {
                    return ability;
                }
            }

            return null;
        }
    }

    /// <summary>Whether <paramref name="ability" /> is this creature's own basic clone.</summary>
    public bool IsBasic(IAbility ability) => Basic is not null && ReferenceEquals(ability, Basic);

    /// <summary>Not being cast, and off cooldown.</summary>
    public static bool IsReady(IAbility ability) => !ability.Casting && ability.CooldownTimer <= 0f;

    /// <summary>
    /// Replaces the creature's abilities with fresh clones of the kit's rows from <paramref name="catalog" />,
    /// off cooldown. An id the catalog does not hold (missing, or refused by it) is logged at Error and left
    /// out, and the creature fights with the rest; an id named twice is loaded once.
    /// </summary>
    public void Load(AbilityCatalog catalog, CreatureAbilityKit kit, ILogger logger, string creatureName)
    {
        List<IAbility> loaded = new(1 + kit.Specials.Length);
        IAbility? basic = null;

        foreach (AbilityId id in (AbilityId[])[kit.Basic, .. kit.Specials])
        {
            if (loaded.Exists(a => a.AbilityId == id))
            {
                continue;
            }

            if (!catalog.TryGet(id, out AbilityTemplate? template))
            {
                logger.LogError("Ability {AbilityId} is missing or was refused by the catalog; creature {CreatureName} fights without it",
                    id.Value, creatureName);
                continue;
            }

            var ability = new GameAbility
            {
                AbilityId = id,
                Metadata = AbilityMetadataMapper.From(template),
                CastTimeTimer = (float)template.CastTime / 1000,
                CooldownTimer = 0f,
            };

            loaded.Add(ability);
            if (id == kit.Basic)
            {
                basic = ability;
            }
        }

        _abilities = [.. loaded];
        Basic = basic;
    }

    /// <summary>Counts every running cooldown down by <paramref name="deltaTime" />. Called once a tick.</summary>
    public void Update(TimeSpan deltaTime)
    {
        float seconds = (float)deltaTime.TotalSeconds;
        foreach (IAbility ability in _abilities)
        {
            if (ability.CooldownTimer > 0f)
            {
                ability.CooldownTimer -= seconds;
            }
        }
    }

    /// <summary>Takes every cooldown off, so no countdown carries from one fight into the next.</summary>
    public void ResetCooldowns()
    {
        foreach (IAbility ability in _abilities)
        {
            ability.CooldownTimer = 0f;
        }
    }
}

using Avalon.Combat;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Public.Abilities;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Talents;

/// <summary>
/// The abilities a character holds, from the talent tree (#1181): its class start's starter first, then the ability each
/// held ability node links, by ascending node id. Nothing else grants one.
/// </summary>
public static class TalentAbilities
{
    /// <summary>
    /// Rebuilds <paramref name="character" />'s abilities from <paramref name="tree" /> and its held nodes, each ability
    /// built from <paramref name="abilities" />. An ability already held keeps its <see cref="IAbility" />, and with it its
    /// cooldown and cast timers; a new one arrives ready (cooldown 0, its cast time). An ability the catalog does not hold
    /// (it refused it, or a reload removed it) is left out with a Warning. When the held ability ids are the same, in the
    /// same order, nothing is built and nothing is allocated.
    /// </summary>
    /// <returns>True when the held ability ids changed.</returns>
    public static bool Rebuild(CharacterEntity character, TalentCatalog tree, AbilityCatalog abilities, ILogger logger)
    {
        CharacterAbilityContainer spells = character.Spells;
        if (spells.All is IReadOnlyList<IAbility> current && Matches(current, character, tree, abilities))
            return false;

        var held = new List<IAbility>();
        if (tree.StarterOf(character.Class) is { } starter)
            Add(held, starter, spells, abilities, logger);

        IReadOnlyList<int> nodes = character.Talents.Held;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (AbilityOf(tree, nodes[i]) is { } ability)
                Add(held, ability, spells, abilities, logger);
        }

        spells.Load(held);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="current" /> holds exactly the abilities a rebuild would, in its order: the starter, then
    /// each held ability node's, each counted only where the catalog holds it.
    /// </summary>
    private static bool Matches(IReadOnlyList<IAbility> current, CharacterEntity character, TalentCatalog tree,
        AbilityCatalog abilities)
    {
        int position = 0;
        if (tree.StarterOf(character.Class) is { } starter && !Next(current, ref position, starter, abilities))
            return false;

        IReadOnlyList<int> nodes = character.Talents.Held;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (AbilityOf(tree, nodes[i]) is { } ability && !Next(current, ref position, ability, abilities))
                return false;
        }

        return position == current.Count;
    }

    // An ability the catalog does not hold is skipped by a rebuild, so it is skipped here too.
    private static bool Next(IReadOnlyList<IAbility> current, ref int position, AbilityId ability, AbilityCatalog abilities)
    {
        if (!abilities.TryGet(ability, out _))
            return true;

        if (position >= current.Count || current[position].AbilityId != ability)
            return false;

        position++;
        return true;
    }

    private static AbilityId? AbilityOf(TalentCatalog tree, int nodeId) =>
        tree.TryIndexOf(nodeId, out int index) && tree.KindAt(index) == TalentNodeKind.Ability ? tree.AbilityAt(index) : null;

    private static void Add(List<IAbility> held, AbilityId id, CharacterAbilityContainer spells, AbilityCatalog abilities,
        ILogger logger)
    {
        if (!abilities.TryGet(id, out AbilityTemplate? template))
        {
            logger.LogWarning("Ability {AbilityId} is missing or was refused by the catalog; the character does not get it",
                id);
            return;
        }

        held.Add(spells[id] ?? new GameAbility
        {
            AbilityId = id,
            Metadata = AbilityMetadataMapper.From(template),
            CastTimeTimer = (float)template.CastTime / 1000,
            CooldownTimer = 0f,
        });
    }
}

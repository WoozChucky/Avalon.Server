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
    /// Rebuilds <paramref name="character" />'s abilities from <paramref name="tree" /> and its held nodes, each
    /// ability built from <paramref name="abilities" />. An ability already held keeps its <see cref="IAbility" />, and
    /// with it its cooldown and cast timers; a new one arrives ready (cooldown 0, its cast time), the one parked when
    /// it last left when there is one built from <paramref name="abilities" /> (Part C), so once warm a rebuild
    /// allocates nothing, the list included: it is rebuilt in place
    /// (<see cref="CharacterAbilityContainer.StartRebuild" />). An ability the catalog does not hold (it refused it, or
    /// a reload removed it) is left out with a Warning. Then each held ability is shaped by the capstones held that
    /// shape it (Part D, <see cref="TreeAbility.Reshape" />): a kept one, an unparked one, from its own unshaped
    /// metadata to the shape held now, and a new one. When the held ability ids are the same, in the same order, and
    /// each is shaped as the capstones held shape it, nothing is built.
    /// </summary>
    /// <returns>True when the held ability ids, or the shape of any held ability, changed.</returns>
    public static bool Rebuild(CharacterEntity character, TalentCatalog tree, AbilityCatalog abilities, ILogger logger)
    {
        CharacterAbilityContainer spells = character.Spells;
        if (Walk(spells.All, character, tree, abilities, reshape: false))
            return false;

        List<IAbility> held = spells.StartRebuild();
        if (tree.StarterOf(character.Class) is { } starter)
            Add(held, starter, spells, abilities, logger);

        IReadOnlyList<int> nodes = character.Talents.Held;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (AbilityOf(tree, nodes[i], out _) is { } ability)
                Add(held, ability, spells, abilities, logger);
        }

        spells.FinishRebuild(abilities);
        Walk(spells.All, character, tree, abilities, reshape: true);
        return true;
    }

    /// <summary>
    /// Walks <paramref name="current" /> against the abilities a rebuild holds, in its order: the starter, then each held
    /// ability node's, each counted only where the catalog holds it. Without <paramref name="reshape" />, whether it holds
    /// exactly those, each <see cref="TreeAbility" /> shaped as the capstones held shape it. With it, after a rebuild,
    /// each <see cref="TreeAbility" /> is reshaped to that shape. An ability that is not one (only tests and scenarios
    /// load those) is never reshaped.
    /// </summary>
    private static bool Walk(IReadOnlyList<IAbility> current, CharacterEntity character, TalentCatalog tree,
        AbilityCatalog abilities, bool reshape)
    {
        TalentAllocation talents = character.Talents;
        int position = 0;
        if (tree.StarterOf(character.Class) is { } starter && !Next(current, ref position, starter,
                ShapingOf(tree, tree.StartIndexOf(character.Class), talents), abilities, reshape))
        {
            return false;
        }

        IReadOnlyList<int> nodes = talents.Held;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (AbilityOf(tree, nodes[i], out int index) is { } ability &&
                !Next(current, ref position, ability, ShapingOf(tree, index, talents), abilities, reshape))
            {
                return false;
            }
        }

        return position == current.Count;
    }

    // An ability the catalog does not hold is skipped by a rebuild, so it is skipped here too.
    private static bool Next(IReadOnlyList<IAbility> current, ref int position, AbilityId ability,
        in AbilityShaping shaping, AbilityCatalog abilities, bool reshape)
    {
        if (!abilities.TryGet(ability, out _))
            return true;

        if (position >= current.Count || current[position].AbilityId != ability)
            return false;

        if (current[position] is TreeAbility shaped)
        {
            if (reshape)
                shaped.Reshape(in shaping);
            else if (shaped.Shaping != shaping)
                return false;
        }

        position++;
        return true;
    }

    /// <summary>
    /// The modifiers of every capstone held that shapes the ability the node at <paramref name="grantIndex" /> grants,
    /// summed; none for an index of -1 (a refused tree's starter).
    /// </summary>
    private static AbilityShaping ShapingOf(TalentCatalog tree, int grantIndex, TalentAllocation talents)
    {
        AbilityShaping shaping = default;
        if (grantIndex < 0)
            return shaping;

        ReadOnlySpan<int> shapers = tree.ShapersAt(grantIndex);
        for (int i = 0; i < shapers.Length; i++)
        {
            if (!talents.Holds(tree.NodeIdAt(shapers[i])))
                continue;

            ReadOnlySpan<TalentModifier> modifiers = tree.ModifiersAt(shapers[i]);
            for (int m = 0; m < modifiers.Length; m++)
                shaping.Add(modifiers[m].Kind, modifiers[m].Value, modifiers[m].Value2);
        }

        return shaping;
    }

    private static AbilityId? AbilityOf(TalentCatalog tree, int nodeId, out int index) =>
        tree.TryIndexOf(nodeId, out index) && tree.KindAt(index) == TalentNodeKind.Ability ? tree.AbilityAt(index) : null;

    private static void Add(List<IAbility> held, AbilityId id, CharacterAbilityContainer spells, AbilityCatalog abilities,
        ILogger logger)
    {
        if (!abilities.TryGet(id, out AbilityTemplate? template))
        {
            logger.LogWarning("Ability {AbilityId} is missing or was refused by the catalog; the character does not get it",
                id);
            return;
        }

        if ((spells[id] ?? spells.Unpark(id, abilities)) is { } reused)
        {
            held.Add(reused);
            return;
        }

        // Unshaped until the walk after the rebuild shapes it.
        AbilityMetadata metadata = AbilityMetadataMapper.From(template);
        held.Add(new TreeAbility
        {
            AbilityId = id,
            Metadata = metadata,
            Base = metadata,
            CastTimeTimer = (float)template.CastTime / 1000,
            CooldownTimer = 0f,
            BuiltFrom = abilities,
        });
    }
}

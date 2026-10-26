using Avalon.World.Configuration;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Items;

/// <summary>
/// What a creature is worth to a worn sealed affix's bar (#1180), built once from Game:Items:Awakening: its rarity's
/// weight, or nothing for one more than the level gap below the character. Read on every metered hit and kill, so it
/// holds the settings as plain fields and allocates nothing.
/// </summary>
public sealed class AwakeningRules
{
    private readonly int _normal, _elite, _rare, _boss, _levelGap;

    public AwakeningRules(AwakeningConfiguration configuration)
    {
        CreatureWeights weights = configuration.Weights;
        (_normal, _elite, _rare, _boss) = (weights.Normal, weights.Elite, weights.Rare, weights.Boss);
        _levelGap = configuration.LevelGap;
    }

    /// <summary>The default settings' rules, for a combat service built without any (tests).</summary>
    public static AwakeningRules Defaults { get; } = new(new AwakeningConfiguration());

    /// <summary>
    /// The points a kill of <paramref name="creature" /> gives a character of <paramref name="characterLevel" />, and the
    /// weight its damage is scaled by: 0 when it is more than the level gap below, else its rarity's weight.
    /// </summary>
    public int WeightOf(ICreature creature, ushort characterLevel)
    {
        if (characterLevel - creature.Level > _levelGap)
            return 0;

        return creature.Metadata.Rarity switch
        {
            CreatureRarity.Elite => _elite,
            CreatureRarity.Rare => _rare,
            CreatureRarity.Boss => _boss,
            _ => _normal,
        };
    }
}

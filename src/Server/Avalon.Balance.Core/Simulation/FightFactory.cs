using Avalon.Combat;
using Avalon.Domain.World;

namespace Avalon.Balance.Core;

public static class FightFactory
{
    /// <summary>One run's fight: the player, then the pack drawn in order from <paramref name="random" />, which then rolls the combat.</summary>
    public static FightSimulator Create(BalanceData data, Scenario scenario, RowKey key, IReadOnlyList<ItemTemplate> worn,
        IReadOnlyList<CompiledRotationEntry> rotation, Random random)
    {
        SimPlayer player = SimPlayer.Create(data, key.Class, key.Level, worn);
        var creatures = new List<SimCreature>();
        foreach (PackEntry entry in scenario.Pack)
        {
            for (int i = 0; i < entry.Count; i++)
            {
                CreatureTemplate template = entry.Template is { } id
                    ? data.Creature(id)
                    : Draw(data.HostileOfRarity(entry.Rarity!.Value), random);
                creatures.Add(SimCreature.Create(data, template, CreatureLevel(template, key.Level, scenario, random), creatures.Count));
            }
        }

        return new FightSimulator(data.Combat.Formula, player, creatures, rotation, new CombatRandom(random), scenario.ConeHits,
            data: data);
    }

    /// <summary>
    /// The player's level plus the offset, clamped to the template's range; or, for "template", a roll of that range as
    /// CreatureSpawner.RollLevel does (at least 1, and the maximum at least the minimum).
    /// </summary>
    public static ushort CreatureLevel(CreatureTemplate template, ushort playerLevel, Scenario scenario, Random random)
    {
        (short min, short max) = LevelRange(template);

        return scenario.OffsetFromTemplate
            ? (ushort)random.Next(min, max + 1)
            : (ushort)Math.Clamp(playerLevel + (scenario.Offset ?? 0), min, max);
    }

    /// <summary>The template's level range as CreatureSpawner.RollLevel reads it: at least 1, the maximum at least the minimum.</summary>
    public static (short Min, short Max) LevelRange(CreatureTemplate template)
    {
        short min = Math.Max((short)1, template.MinLevel);
        return (min, Math.Max(min, template.MaxLevel));
    }

    private static CreatureTemplate Draw(IReadOnlyList<CreatureTemplate> templates, Random random) =>
        templates[random.Next(templates.Count)];
}

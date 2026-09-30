using System.Reflection;
using Avalon.World.Creatures;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Scripts.Creatures.Forest;

namespace Avalon.Balance.Data;

/// <summary>Each creature script's declared kit, read from the script itself by name, as ScriptManager names them.</summary>
public static class CreatureKits
{
    /// <summary>
    /// Specials a script casts only from range. BlightflySwarmlingScript spits blight only while Sting cannot reach;
    /// in the simulator everyone is in melee, so it never does.
    /// </summary>
    public static readonly IReadOnlySet<uint> RangedOnly = new HashSet<uint> { BlightflySwarmlingScript.BlightSpit.Value };

    /// <summary>
    /// Avalon.World's top-level types by name, read once: For runs for every creature of every fight, from parallel
    /// rows, so it must neither scan the assembly each time nor write shared state.
    /// </summary>
    private static readonly Dictionary<string, Type> TopLevelTypes = typeof(AggroDefendScript).Assembly.GetTypes()
        .Where(t => !t.IsNested)
        .GroupBy(t => t.Name, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>The kit of the top-level script type named <paramref name="scriptName" />, or null when it declares none.</summary>
    public static CreatureAbilityKit? For(string? scriptName)
    {
        if (string.IsNullOrEmpty(scriptName) || !TopLevelTypes.TryGetValue(scriptName, out Type? type))
            return null;

        PropertyInfo? kit = type.GetProperty("Kit", BindingFlags.Public | BindingFlags.Static);
        return kit?.GetValue(null) as CreatureAbilityKit;
    }
}

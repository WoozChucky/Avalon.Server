using System.Reflection;
using Avalon.Balance.Core;
using Avalon.World.Creatures;
using Avalon.World.Scripts.Creatures;
using Avalon.World.Scripts.Creatures.Forest;

namespace Avalon.Server.World.UnitTests.Balance;

/// <summary>Keeps the balance tool's static creature kit table equal to the kits the world scripts declare.</summary>
public class CreatureKitParityShould
{
    [Fact]
    public void Match_every_script_kit()
    {
        Dictionary<string, CreatureAbilityKit> scripts = typeof(AggroDefendScript).Assembly.GetTypes()
            .Where(t => !t.IsNested)
            .Select(t => (t.Name, Kit: t.GetProperty("Kit", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as CreatureAbilityKit))
            .Where(p => p.Kit is not null)
            .ToDictionary(p => p.Name, p => p.Kit!);

        Assert.Equal(scripts.Keys.Order(), CreatureKits.ByScript.Keys.Order());
        foreach ((string name, CreatureAbilityKit kit) in scripts)
        {
            Assert.Equal(kit.Basic, CreatureKits.ByScript[name].Basic);
            Assert.Equal(kit.Specials, CreatureKits.ByScript[name].Specials);
        }

        Assert.Equal([BlightflySwarmlingScript.BlightSpit], CreatureKits.ByScript[nameof(BlightflySwarmlingScript)].RangedOnly);
        Assert.All(CreatureKits.ByScript.Where(k => k.Key != nameof(BlightflySwarmlingScript)), k => Assert.Empty(k.Value.RangedOnly));
    }
}

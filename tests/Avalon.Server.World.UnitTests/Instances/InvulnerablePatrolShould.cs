using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// #600: a patrolling creature fights when hit, but an invulnerable one (a town walker) never is:
/// the combat service refuses the hit before any script hears of it, so its patrol goes on.
/// </summary>
public class InvulnerablePatrolShould
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1d / 60d);

    [Fact]
    public void Ignore_Hits_And_Keep_Patrolling()
    {
        // Open ground: every leg is one straight step to its point.
        var navigator = Substitute.For<IMapNavigator>();
        navigator.FindPath(Arg.Any<Vector3>(), Arg.Any<Vector3>()).Returns(ci => [ci.ArgAt<Vector3>(1)]);
        using MapInstance instance = TestMapInstances.Build(NewWorld(), navigator: navigator);
        MapInstanceClient attacker = Join(instance, 600_201);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 600_021),
            Metadata = LootTestData.BoarTemplate(null),
            Position = Vector3.zero,
            Health = 10,
            CurrentHealth = 10,
            Invulnerable = true,
            PatrolPath = [new PatrolPoint(new Vector3(5f, 0f, 0f), TimeSpan.Zero), new PatrolPoint(new Vector3(10f, 0f, 0f), TimeSpan.Zero)],
        };
        instance.AddCreature(creature);
        var script = new CreaturePatrolScript(NullLoggerFactory.Instance, creature, instance);
        creature.Script = script;
        instance.Update(Tick);

        instance.CombatService.ApplyDamage(attacker.Character, creature, 10);
        for (int i = 0; i < 5; i++)
            instance.Update(Tick);

        Assert.Equal(10u, creature.CurrentHealth);
        Assert.Null(instance.CombatService.GetEncounterFor(creature));
        Assert.Same(script, creature.Script);
        Assert.Equal(CreaturePatrolScript.PatrolState.Patrolling, script.State);
    }
}

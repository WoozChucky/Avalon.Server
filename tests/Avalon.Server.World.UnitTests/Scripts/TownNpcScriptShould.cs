using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Scripts;
using Avalon.World.Public.Units;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// The AI a town NPC runs: stand still, never aggro, never fight.
/// </summary>
public class TownNpcScriptShould
{
    /// <summary>
    /// The trap this test exists for. <c>CreaturePlacementService.AttachScript</c> builds every AI
    /// script with <c>ActivatorUtilities.CreateInstance(sp, type, creature, instance)</c> — exactly
    /// two runtime arguments, everything else resolved from DI. A script needing a third plain
    /// argument throws there, the throw is swallowed by AttachScript's catch, and the creature ends
    /// up in the world with no script at all.
    ///
    /// Both existing passive scripts have that shape: <c>CreatureIdleScript</c> takes a
    /// <c>float idleTime</c> and <c>CreaturePatrolScript</c> takes a <c>Vector3[] waypoints</c>.
    /// Neither can be built from a <c>ScriptName</c> string in seed data. Because seed data names
    /// scripts by string, the compiler cannot catch it, and the failure is a log line rather than a
    /// crash — so it goes unnoticed. This test walks the real resolution and construction path.
    /// </summary>
    [Fact]
    public void Be_Resolvable_By_Name_And_Constructible_The_Way_Placement_Builds_Scripts()
    {
        var manager = new ScriptManager(NullLoggerFactory.Instance);
        manager.Load();

        Type? scriptType = manager.GetAiScript(nameof(TownNpcScript));
        Assert.NotNull(scriptType);

        ServiceProvider services = new ServiceCollection().BuildServiceProvider();

        // Argument-for-argument identical to AttachScript.
        object built = ActivatorUtilities.CreateInstance(
            services, scriptType, Substitute.For<ICreature>(), Substitute.For<IMapInstance>());

        Assert.IsAssignableFrom<AiScript>(built);
    }

    [Fact]
    public void Leave_The_Creature_Untouched_When_A_Player_Walks_Into_Range()
    {
        // A town NPC does not turn hostile because someone stood next to it. AggroDefendScript and
        // CreatureCombatScript both take a target here; this one must not.
        ICreature creature = Substitute.For<ICreature>();
        var script = new TownNpcScript(creature, Substitute.For<IMapInstance>());

        script.OnEnteredRange(Substitute.For<Avalon.World.Public.Characters.ICharacter>());
        script.Update(TimeSpan.FromSeconds(1));

        creature.DidNotReceiveWithAnyArgs().Position = default;
        creature.DidNotReceiveWithAnyArgs().Velocity = default;
    }

    [Fact]
    public void Never_Subtract_Health_When_Hit()
    {
        // The second layer under ICreature.Invulnerable. Creature.OnHit forwards straight to the
        // script, and CreatureCombatScript.OnHit is where a creature's health is actually reduced —
        // so a script that does not implement it cannot kill its creature even if the guard in
        // ApplyDamageCore were ever bypassed.
        ICreature creature = Substitute.For<ICreature>();
        var script = new TownNpcScript(creature, Substitute.For<IMapInstance>());

        script.OnHit(Substitute.For<IUnit>(), 999u);

        creature.DidNotReceiveWithAnyArgs().CurrentHealth = default;
    }
}

using Avalon.World.Public.Creatures;
using Avalon.World.Public.Instances;
using Avalon.World.Public.Units;
using Avalon.World.Scripts.Creatures;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Scripts;

/// <summary>
/// The AI a town NPC runs: stand still, never aggro, never fight.
/// </summary>
public class TownNpcScriptShould
{
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

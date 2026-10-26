using Avalon.Combat;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.World.Characters;
using Avalon.World.Instances;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// #1181: mana, every class's one pool, is kept through death, a revive and every instance transfer, and does not
/// regenerate while dead. Character ids 526_3xx.
/// </summary>
public class ManaLifecycleShould
{
    [Fact]
    public void Keep_mana_through_death_and_an_instance_transfer()
    {
        using MapInstance first = TestMapInstances.Build(NewWorld());
        using MapInstance second = TestMapInstances.Build(NewWorld());
        MapInstanceClient warrior = Join(first, 526_321);
        warrior.Character.PowerType = PowerType.Mana;
        warrior.Character.ApplyStats(new DerivedCharacterStats(MaxHealth: 100, MaxPower: 100, Stamina: 0, Strength: 0,
            Agility: 0, Intellect: 0, Armor: 0, BlockPct: 0f, Evasion: 0, CritPct: 0f), CurrentValues.EnterWorld,
            TestCombat.Formula);
        warrior.Character.CurrentPower = 60;

        warrior.Character.IsDead = true;
        Assert.Equal(60u, warrior.Character.CurrentPower);
        warrior.Character.Update(TimeSpan.FromSeconds(1));   // the dead regenerate nothing
        Assert.Equal(60u, warrior.Character.CurrentPower);

        warrior.Character.Revive();
        Assert.Equal(60u, warrior.Character.CurrentPower);

        first.RemoveCharacter(warrior.Connection);
        warrior.Character.InstanceId = second.InstanceId;
        second.AddCharacter(warrior.Connection);

        Assert.Equal(60u, warrior.Character.CurrentPower);
    }
}

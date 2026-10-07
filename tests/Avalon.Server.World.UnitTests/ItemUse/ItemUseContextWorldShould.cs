using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.Quest;
using Avalon.Network.Packets.State;
using Avalon.Server.World.UnitTests.Abilities;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.Server.World.UnitTests.Quests;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Public;
using Avalon.World.Respawn;
using Avalon.World.Scripts.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>The toolbox members that act on the user's instance: damage, casts, party members and quests.</summary>
public class ItemUseContextWorldShould
{
    private static ItemUseContext ContextFor(IWorld world, IWorldConnection connection, CharacterEntity character,
        IItemUseHost host, Avalon.World.Quests.QuestService? quests = null) => new(
        connection, character, host, TestCharacters.Item(0, TestCharacters.Potion), TestCharacters.Potion,
        new ItemUseTools(world, new CharacterEconomy(world, new ItemIdAllocator()),
            new TownReturn(NullLogger.Instance, world, Substitute.For<IRespawnTargetResolver>(), Substitute.For<IChunkLibrary>()),
            new MapTeleport(NullLogger<MapTeleport>.Instance, world, Substitute.For<IChunkLibrary>(), Substitute.For<Avalon.World.Persistence.ICharacterSaver>()),
            Substitute.For<ICreaturePlacementService>(), new FakeTimeProvider(), NullLogger<ItemUseTools>.Instance,
            quests: quests));

    private static Creature AddBoar(MapInstance instance, uint id, Vector3 at, bool invulnerable = false)
    {
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, id),
            Metadata = Loot.LootTestData.BoarTemplate(null),
            Position = at,
            Health = 100,
            CurrentHealth = 100,
            Level = 1,
            Invulnerable = invulnerable,
        };
        instance.AddCreature(boar);
        return boar;
    }

    [Fact]
    public void Find_the_hostiles_around_the_user_and_damage_only_those()
    {
        IWorld world = NewWorld();
        using MapInstance instance = TestMapInstances.Build(world);
        MapInstanceClient user = Join(instance, 7);
        Creature near = AddBoar(instance, 90, new Vector3(0, 0, 2));
        Creature far = AddBoar(instance, 91, new Vector3(0, 0, 40));
        Creature npc = AddBoar(instance, 92, new Vector3(0, 0, 1), invulnerable: true);
        // A script, so the hit lowers its health.
        near.Script = new CreatureCombatScript(NullLoggerFactory.Instance, near, instance);
        ItemUseContext ctx = ContextFor(world, user.Connection, user.Character, instance);

        Assert.Equal([near.Guid], ctx.HostilesAround(5f));
        Assert.Empty(ctx.HostilesAround(float.NaN));
        Assert.False(ctx.Damage(npc.Guid, 10));                // not hostile
        Assert.False(ctx.Damage(user.Character.Guid, 10));     // never the user
        Assert.False(ctx.Damage(new ObjectGuid(ObjectType.Creature, 999), 10));   // not in this instance
        Assert.False(ctx.Damage(near.Guid, 0));
        Assert.False(user.Character.IsInCombat);

        Assert.True(ctx.Damage(near.Guid, 10));                // a raw hit through the combat service
        Assert.True(near.CurrentHealth < 100u);
        Assert.True(user.Character.IsInCombat);
        Assert.True(ctx.Damage(far.Guid, 10));                 // no range on Damage itself: the script picked it
    }

    /// <summary>A creature walking home ignores hits, so Damage says it did nothing.</summary>
    [Fact]
    public void Answer_false_for_damage_on_a_creature_walking_home()
    {
        IWorld world = NewWorld();
        using MapInstance instance = TestMapInstances.Build(world);
        MapInstanceClient user = Join(instance, 7);
        Creature boar = AddBoar(instance, 93, new Vector3(0, 0, 2));
        var script = new CreatureCombatScript(NullLoggerFactory.Instance, boar, instance);
        boar.Script = script;
        script.State = CreatureCombatScript.CombatState.Returning;
        ItemUseContext ctx = ContextFor(world, user.Connection, user.Character, instance);

        Assert.False(ctx.Damage(boar.Guid, 10));
        Assert.Equal(100u, boar.CurrentHealth);
        Assert.False(user.Character.IsInCombat);
    }

    [Fact]
    public void Cast_a_held_ability_through_the_cast_system_free_or_paid()
    {
        MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _);
        using (instance)
        {
            MapInstanceClient user = Join(instance, 7);
            var row = AbilityTestData.Circle(1);
            row.Cost = 30;
            row.CostPowerType = PowerType.Mana;
            var held = AbilityTestData.Game(row);
            user.Character.Spells.Load([held]);
            user.Character.PowerType = PowerType.Mana;
            user.Character.Power = 100;
            user.Character.CurrentPower = 10;
            ItemUseContext ctx = ContextFor(NewWorld(), user.Connection, user.Character, instance);

            Assert.False(ctx.CastAbility(new AbilityId(1)));              // 10 Mana cannot pay 30
            Assert.False(user.Character.IsInCombat);                       // a refused cast marks nothing
            Assert.True(ctx.CastAbility(new AbilityId(1), free: true));
            Assert.Equal(10u, user.Character.CurrentPower);
            Assert.True(user.Character.IsInCombat);

            held.CooldownTimer = 5f;
            Assert.False(ctx.CastAbility(new AbilityId(1), free: true)); // free never skips a running cooldown
        }
    }

    [Fact]
    public async Task Cast_an_instant_ability_the_user_does_not_hold_and_refuse_one_with_a_cast_time()
    {
        var instant = AbilityTestData.Circle(5);
        var windUp = AbilityTestData.Circle(6);
        windUp.CastTime = 1000;
        StaticData data = await TestStaticData.LoadAsync(TestStaticData.Repositories(abilities: () => [instant, windUp]));
        IWorld world = NewWorld(data);
        MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _, world: world);
        using (instance)
        {
            MapInstanceClient user = Join(instance, 7);
            ItemUseContext ctx = ContextFor(world, user.Connection, user.Character, instance);

            Assert.True(ctx.CastAbility(new AbilityId(5)));
            Assert.False(ctx.CastAbility(new AbilityId(6)));   // its queued cast would not show as the user's
            Assert.False(ctx.CastAbility(new AbilityId(77)));  // not in the catalog
            Assert.Single(user.Read<SUnitFinishCastPacket>(NetworkPacketType.SMSG_UNIT_FINISH_CAST));
        }
    }

    /// <summary>The cast system's queue does not check for a cast under way, so the context refuses one itself.</summary>
    [Fact]
    public void Refuse_a_cast_while_the_user_is_dead_or_already_casting()
    {
        MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _);
        using (instance)
        {
            MapInstanceClient user = Join(instance, 7);
            var windUp = AbilityTestData.Circle(3);
            windUp.CastTime = 1000;
            user.Character.Spells.Load([AbilityTestData.Game(windUp), AbilityTestData.Game(AbilityTestData.Circle(4))]);
            ItemUseContext ctx = ContextFor(NewWorld(), user.Connection, user.Character, instance);

            user.Character.IsDead = true;
            Assert.False(ctx.CastAbility(new AbilityId(4)));
            user.Character.IsDead = false;

            Assert.True(ctx.CastAbility(new AbilityId(3)));     // a held ability may have a cast time
            Assert.True(user.Character.Spells.IsCasting);
            Assert.False(ctx.CastAbility(new AbilityId(4)));    // one cast at a time
        }
    }

    [Fact]
    public void Refuse_a_cursor_ability_without_a_point()
    {
        MapInstance instance = TestMapInstances.BuildCasting(out CastAbilityHandler _);
        using (instance)
        {
            MapInstanceClient user = Join(instance, 7);
            user.Character.Spells.Load([AbilityTestData.Game(AbilityTestData.AimedCircle(2))]);
            ItemUseContext ctx = ContextFor(NewWorld(), user.Connection, user.Character, instance);

            Assert.False(ctx.CastAbility(new AbilityId(2)));
            Assert.True(ctx.CastAbility(new AbilityId(2), point: new Vector3(0, 0, 5)));
        }
    }

    [Fact]
    public void Heal_and_restore_party_members_in_the_same_instance_only()
    {
        var party = new PartyTestWorld();
        PartyClient a = party.Online(1, "A");
        PartyClient b = party.Online(2, "B");
        PartyClient c = party.Online(3, "C");
        party.Form(a, b);
        IWorld world = NewWorld();
        using MapInstance instance = TestMapInstances.Build(world);
        foreach (PartyClient member in new[] { a, b, c })
        {
            member.Character.Spells.Load([]);
            member.Character.InstanceId = instance.InstanceId;
            instance.AddCharacter(member.Connection);
        }

        b.Character.Health = 100;
        b.Character.CurrentHealth = 50;
        b.Character.PowerType = PowerType.Mana;
        b.Character.Power = 100;
        b.Character.CurrentPower = 0;
        c.Character.Health = 100;
        c.Character.CurrentHealth = 50;
        ItemUseContext ctx = ContextFor(world, a.Connection, a.Character, instance);

        Assert.Equal([b.Character.Guid], ctx.PartyMembersHere());
        Assert.True(ctx.InParty);
        Assert.Equal(30u, ctx.RestoreHealthOf(b.Character.Guid, 30));
        Assert.Equal(80u, b.Character.CurrentHealth);
        Assert.Equal(20u, ctx.RestorePowerOf(b.Character.Guid, 20));
        Assert.Equal(0u, ctx.RestoreHealthOf(c.Character.Guid, 30));   // not in the party
        Assert.Equal(50u, c.Character.CurrentHealth);
        Assert.Equal(0u, ctx.RestoreHealthOf(a.Character.Guid, 30));   // the user is not its own member

        b.Character.CurrentHealth = 0;
        b.Character.IsDead = true;
        Assert.Equal(0u, ctx.RestoreHealthOf(b.Character.Guid, 30));   // never the dead
        Assert.Equal(0u, ctx.RestorePowerOf(b.Character.Guid, 20));
        Assert.Equal(0u, b.Character.CurrentHealth);
    }

    [Fact]
    public async Task Start_and_advance_a_quest_through_the_quest_service()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        QuestClient c = w.Join();
        ItemUseContext ctx = ContextFor(w.World, c.Connection, c.Character, Substitute.For<IItemUseHost>(), w.Quests);

        Assert.Equal(QuestResult.Ok, ctx.StartQuest(QuestTestData.Hunt));
        Assert.True(ctx.AdvanceQuest(QuestTestData.Hunt, QuestTestData.HuntKill));
        Assert.Equal(1u, c.Character.Quests.Get(QuestTestData.Hunt)!.ProgressOf(QuestTestData.HuntKill));
        Assert.False(ctx.AdvanceQuest(QuestTestData.Tusks, QuestTestData.TusksCollect));   // not held, and Collect anyway
        Assert.Equal(QuestResult.NotAvailable, ctx.StartQuest(QuestTestData.Hunt));       // already held
    }

    [Fact]
    public async Task Apply_a_helpful_aura_to_the_user_or_a_party_member_here_and_to_nobody_else()
    {
        var party = new PartyTestWorld();
        PartyClient a = party.Online(1, "A");
        PartyClient b = party.Online(2, "B");
        PartyClient c = party.Online(3, "C");
        party.Form(a, b);
        IWorld world = NewWorld(await TestStaticData.LoadAsync(TestStaticData.Repositories(
            auras: () => [Auras.AuraTestData.Renew(), Auras.AuraTestData.Bleed()])));
        using MapInstance instance = TestMapInstances.Build(world);
        foreach (PartyClient member in new[] { a, b, c })
        {
            member.Character.Spells.Load([]);
            member.Character.InstanceId = instance.InstanceId;
            member.Character.Health = 100;
            member.Character.CurrentHealth = 50;
            instance.AddCharacter(member.Connection);
        }

        ItemUseContext ctx = ContextFor(world, a.Connection, a.Character, instance);

        Assert.True(ctx.ApplyAura(new AuraId(904)));
        Assert.True(ctx.ApplyAura(new AuraId(904), b.Character.Guid));
        Assert.False(ctx.ApplyAura(new AuraId(904), c.Character.Guid));   // not in the party
        Assert.False(ctx.ApplyAura(new AuraId(901)));                     // harmful: never from an item
        Assert.False(ctx.ApplyAura(new AuraId(999)));                     // not loaded
        Assert.Equal((1, 1, 0), (a.Character.Auras.Count, b.Character.Auras.Count, c.Character.Auras.Count));
        Assert.Equal(a.Character.Guid, b.Character.Auras.All[0].CasterGuid);

        a.Character.IsDead = true;
        Assert.False(ctx.ApplyAura(new AuraId(904), b.Character.Guid));
    }
}

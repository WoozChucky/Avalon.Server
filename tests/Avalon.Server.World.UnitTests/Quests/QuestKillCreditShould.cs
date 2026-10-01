using Avalon.Common;
using Avalon.Combat;
using Avalon.Common.Mathematics;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Entities;
using Avalon.World.Configuration;
using Avalon.World.Instances;
using Avalon.World.Loot;
using Avalon.World.Public.Maps;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A kill through a real MapInstance (#433) credits every character that shares it (PartyEligibility) and has a
/// Kill objective for that creature in its quest's current stage: +1 each, capped.
/// </summary>
public class QuestKillCreditShould
{
    private static uint _nextCreature = 960_000;

    private static Creature Spawn(MapInstance instance, QuestTestWorld w, ulong template)
    {
        CreatureTemplate metadata = w.Data.CreatureTemplates.Single(t => t.Id.Value == template);
        var creature = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, Interlocked.Increment(ref _nextCreature)),
            Metadata = metadata,
            TemplateId = metadata.Id,
            Position = Vector3.zero,
            Level = 1,
            Experience = 10,
        };
        instance.AddCreature(creature);
        return creature;
    }

    private static (QuestTestWorld W, PartyTestWorld P, MapInstance Instance, PartyClient A, PartyClient B) PartyOfTwo(QuestTestWorld w)
    {
        var p = new PartyTestWorld();
        PartyClient a = p.Online(1, "A", level: 1);
        PartyClient b = p.Online(2, "B", level: 1);
        p.Form(a, b);
        MapInstance instance = TestMapInstances.Build(w.World, ownerPartyId: p.Parties.PartyOf(a.Id)!.Id, parties: p.Parties, quests: w.Quests);
        MapInstanceClients.Join(instance, a.Character);
        MapInstanceClients.Join(instance, b.Character);
        a.Character.Position = Vector3.zero;
        b.Character.Position = Vector3.zero;
        return (w, p, instance, a, b);
    }

    [Fact]
    public async Task Credit_the_killer_outside_a_party()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        using MapInstance instance = TestMapInstances.Build(w.World, quests: w.Quests);
        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        a.Character.Quests.Start(Hunt, DateTime.UnixEpoch);

        instance.ReportKill(Spawn(instance, w, Boar), a.Character);

        Assert.Equal(1u, a.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
        Assert.Contains("Things done: 1/2", a.Character.Quests.PendingLines);
    }

    [Fact]
    public async Task Credit_every_eligible_member_who_has_the_objective()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (_, _, MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(Hunt, DateTime.UnixEpoch);
            b.Character.Quests.Start(Hunt, DateTime.UnixEpoch);

            instance.ReportKill(Spawn(instance, w, Boar), a.Character);

            Assert.Equal(1u, a.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
            Assert.Equal(1u, b.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
        }
    }

    /// <summary>Review Focus 4: only a current stage's Kill objective for that creature counts.</summary>
    [Fact]
    public async Task Credit_only_members_whose_current_stage_has_the_objective()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (_, _, MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(Hunt, DateTime.UnixEpoch);                               // boars
            var howl = b.Character.Quests.Start(Howl, DateTime.UnixEpoch);                   // wolves in stage 0 ...
            b.Character.Quests.SetStage(howl, 1);                                             // ... but B is past it

            instance.ReportKill(Spawn(instance, w, Wolf), a.Character);
            Assert.Equal(0u, b.Character.Quests.Get(Howl)!.ProgressOf(HowlKill));
            Assert.Equal(0u, a.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));

            instance.ReportKill(Spawn(instance, w, Boar), a.Character);
            Assert.Equal(1u, a.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
            Assert.Empty(b.Character.Quests.Get(Howl)!.Progress);
        }
    }

    [Fact]
    public async Task Credit_a_dead_member_who_shares_the_kill()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (_, _, MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(w);
        using (instance)
        {
            b.Character.Quests.Start(Hunt, DateTime.UnixEpoch);
            b.Character.IsDead = true;   // dead members share a kill (PartyEligibility)

            instance.ReportKill(Spawn(instance, w, Boar), a.Character);

            Assert.Equal(1u, b.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
        }
    }

    /// <summary>Review Focus 4: a killer in a leave countdown shares nothing, so nobody is credited.</summary>
    [Fact]
    public async Task Credit_nobody_for_a_kill_by_a_character_in_a_leave_countdown()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        (_, PartyTestWorld p, MapInstance instance, PartyClient a, PartyClient b) = PartyOfTwo(w);
        using (instance)
        {
            a.Character.Quests.Start(Hunt, DateTime.UnixEpoch);
            b.Character.Quests.Start(Hunt, DateTime.UnixEpoch);
            p.Instances.Owned[instance.InstanceId] = instance.OwnerPartyId!;
            p.Parties.Leave(a.Id);
            Assert.True(p.Parties.InCountdown(a.Id));

            instance.ReportKill(Spawn(instance, w, Boar), a.Character);

            Assert.Empty(a.Character.Quests.Get(Hunt)!.Progress);
            Assert.Empty(b.Character.Quests.Get(Hunt)!.Progress);
        }
    }

    [Fact]
    public async Task Pass_over_a_quest_the_catalog_no_longer_has()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        using MapInstance instance = TestMapInstances.Build(w.World, quests: w.Quests);
        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        a.Character.Quests.Start(99999, DateTime.UnixEpoch);
        a.Character.Quests.Start(Hunt, DateTime.UnixEpoch);

        instance.ReportKill(Spawn(instance, w, Boar), a.Character);

        Assert.Equal(1u, a.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
    }

    /// <summary>Review fix: a member beyond PartyEligibilityRange on X/Z and outside the encounter does not share the kill.</summary>
    [Fact]
    public async Task Credit_no_member_out_of_range_and_outside_the_encounter()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        var p = new PartyTestWorld();
        PartyClient a = p.Online(1, "A", level: 1);
        PartyClient b = p.Online(2, "B", level: 1);
        PartyClient far = p.Online(3, "Far", level: 1);
        p.Form(a, b, far);
        using MapInstance instance = TestMapInstances.Build(w.World, ownerPartyId: p.Parties.PartyOf(a.Id)!.Id, parties: p.Parties, quests: w.Quests);
        MapInstanceClients.Join(instance, a.Character);
        MapInstanceClients.Join(instance, b.Character);
        MapInstanceClients.Join(instance, far.Character);
        a.Character.Position = Vector3.zero;
        b.Character.Position = Vector3.zero;
        far.Character.Position = new Vector3(50f, 0f, 50f);   // 70.7 m on X/Z, past the 60 m range
        Assert.True(w.Config.PartyEligibilityRange < 70f);
        a.Character.Quests.Start(Hunt, DateTime.UnixEpoch);
        far.Character.Quests.Start(Hunt, DateTime.UnixEpoch);

        instance.ReportKill(Spawn(instance, w, Boar), a.Character);   // no hit was dealt: nobody is in an encounter

        Assert.Equal(1u, a.Character.Quests.Get(Hunt)!.ProgressOf(HuntKill));
        Assert.Empty(far.Character.Quests.Get(Hunt)!.Progress);
    }

    /// <summary>Ruling: a quest credit that throws costs the kill nothing, so its loot still drops and its experience is still awarded.</summary>
    [Fact]
    public async Task Still_drop_the_loot_and_award_the_experience_when_quest_credit_throws()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync();
        var roller = Substitute.For<ILootRoller>();
        roller.Roll(default!, default!, default!).ReturnsForAnyArgs([new RolledDrop(null, 0, 5)]);
        var allocator = new PartyLootAllocator(Options.Create(new GameConfiguration()), new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            CombatRandom.Steady);
        using MapInstance instance = TestMapInstances.Build(w.World, navigator: Substitute.For<IMapNavigator>(),
            quests: w.ThrowingQuests(), lootRoller: roller, lootAllocator: allocator);
        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        a.Character.Quests.Start(Hunt, DateTime.UnixEpoch);
        ulong before = a.Character.Experience;

        instance.ReportKill(Spawn(instance, w, Boar), a.Character);

        Assert.Equal(1, instance.Drops.Count);
        Assert.Equal(before + 10, a.Character.Experience);
        Assert.Empty(a.Character.Quests.Get(Hunt)!.Progress);
    }
}

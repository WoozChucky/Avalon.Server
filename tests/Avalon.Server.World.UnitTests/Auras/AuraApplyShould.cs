using System.Reflection;
using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.Network.Packets.Auras;
using Avalon.Server.World.UnitTests.Combat;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Public.Units;
using Avalon.World.Testing;

namespace Avalon.Server.World.UnitTests.Auras;

/// <summary>Applying an aura: new, refreshed or stacked by its row's stacking, refused on what cannot hold it.</summary>
public class AuraApplyShould
{
    private readonly AuraHarness _h = new();

    public AuraApplyShould() => _h.Use(AuraTestData.Bleed(), AuraTestData.Burn(), AuraTestData.Crippled(),
        AuraTestData.Renew(), AuraTestData.Fortified(), AuraTestData.Independent());

    [Fact]
    public void Apply_a_new_aura_with_its_snapshot_and_schedule()
    {
        CharacterEntity warrior = _h.Player(909_101);
        Creature boar = _h.Creature(909_901);

        Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None));

        ActiveAura bleed = Assert.Single(boar.Auras.All);
        Assert.Equal((1u, 4, AuraHarness.T0.AddSeconds(12)), (bleed.Stacks, bleed.Schedule.TicksLeft, bleed.Schedule.ExpiresAt));
        Assert.Equal(warrior.Guid, bleed.CasterGuid);
        Assert.Equal(3f, bleed.Snapshot.PerTickPerStack);   // 12 base over 4 ticks; a character with no stats brings 0
        Assert.Equal(AuraChangeKind.Applied, Assert.Single(boar.Auras.Changes).Kind);
    }

    /// <summary>
    /// A creature's poison: 3 + 1.0 x one roll of its natural 4..8, drawn from the combat service's random when the aura
    /// is applied (6 here), over 3 ticks. The random proves it was drawn once, over that range, and nothing else was.
    /// </summary>
    /// <summary>
    /// A creature's poison (#1178): its source, an Attack, draws one roll of the creature's natural damage, whole, at
    /// application: (3 + 6) over 3 ticks.
    /// </summary>
    [Fact]
    public void Roll_a_creature_casters_natural_damage_once_into_the_snapshot()
    {
        ScriptedCombatRandom random = new ScriptedCombatRandom().Longs(6);
        var h = new AuraHarness(random: random);
        AuraTemplate poison = AuraTestData.Burn(907);
        poison.PeriodicBase = 3f;
        poison.PeriodicEffectiveness = 100f;
        h.Use(poison);
        Creature blightfly = h.Creature(909_990);
        blightfly.DamageMin = 4;
        blightfly.DamageMax = 8;
        CharacterEntity target = h.Player(909_190);
        var spit = new AuraSource(null, 1f, 0f,
            Ability: new Avalon.World.Public.Abilities.AbilityMetadata { Name = "Spit", Tags = AbilityTags.Attack | AbilityTags.Projectile });

        Assert.Equal(AuraApplyResult.Applied, h.Auras.Apply(blightfly, target, new AuraId(907), spit));

        Assert.Equal(3f, Assert.Single(target.Auras.All).Snapshot.PerTickPerStack, precision: 4);
        Assert.Equal([(4L, 8L)], random.WeaponRolls);
        Assert.Equal(0, random.DoublesDrawn);
    }

    /// <summary>A refused aura draws nothing: the roll is taken only once the aura applies.</summary>
    [Fact]
    public void Draw_no_roll_for_an_aura_it_refuses()
    {
        var random = new ScriptedCombatRandom();
        var h = new AuraHarness(maxPerUnit: 1, random: random);
        AuraTemplate poison = AuraTestData.Burn(907);
        poison.PeriodicEffectiveness = 100f;
        h.Use(poison, AuraTestData.Fortified());
        Creature blightfly = h.Creature(909_991);
        blightfly.DamageMin = 4;
        blightfly.DamageMax = 8;
        CharacterEntity target = h.Player(909_191);
        h.Auras.Apply(null, target, new AuraId(905), AuraSource.None);

        var spit = new AuraSource(null, 1f, 0f,
            Ability: new Avalon.World.Public.Abilities.AbilityMetadata { Name = "Spit", Tags = AbilityTags.Attack | AbilityTags.Projectile });
        Assert.Equal(AuraApplyResult.Refused, h.Auras.Apply(blightfly, target, new AuraId(907), spit));

        Assert.Empty(random.WeaponRolls);
    }

    /// <summary>A stacking aura cast again and again: each cast refreshes it; the stacks stop at the cap.</summary>
    [Fact]
    public void Stop_at_max_stacks_and_refresh_each_time()
    {
        CharacterEntity warrior = _h.Player(909_102);
        Creature boar = _h.Creature(909_902);
        List<AuraApplyResult> results = [];

        for (int cast = 0; cast < 5; cast++)
        {
            results.Add(_h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None));
            _h.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal([AuraApplyResult.Applied, AuraApplyResult.Stacked, AuraApplyResult.Stacked, AuraApplyResult.Refreshed,
            AuraApplyResult.Refreshed], results);
        ActiveAura bleed = Assert.Single(boar.Auras.All);
        Assert.Equal(3u, bleed.Stacks);
        Assert.Equal(AuraHarness.T0.AddSeconds(4 + 12), bleed.Schedule.ExpiresAt);   // renewed by the last cast
        Assert.Equal(4, bleed.Schedule.TicksLeft);
        Assert.Equal([AuraChangeKind.Applied, AuraChangeKind.Stacked, AuraChangeKind.Stacked, AuraChangeKind.Refreshed,
            AuraChangeKind.Refreshed], boar.Auras.Changes.Select(c => c.Kind));
    }

    [Fact]
    public void Refresh_a_refresh_aura_and_take_its_latest_caster()
    {
        CharacterEntity first = _h.Player(909_103);
        CharacterEntity second = _h.Player(909_104);
        Creature boar = _h.Creature(909_903);

        _h.Auras.Apply(first, boar, new AuraId(902), AuraSource.None);
        Assert.Equal(AuraApplyResult.Refreshed, _h.Auras.Apply(second, boar, new AuraId(902), AuraSource.None));

        Assert.Equal(second.Guid, Assert.Single(boar.Auras.All).CasterGuid);
    }

    /// <summary>A stack aura is one copy per unit: a second caster stacks the first one's copy and takes it over.</summary>
    [Fact]
    public void Stack_one_copy_whoever_casts_a_stack_aura()
    {
        CharacterEntity first = _h.Player(909_115);
        CharacterEntity second = _h.Player(909_116);
        Creature boar = _h.Creature(909_912);

        _h.Auras.Apply(first, boar, new AuraId(901), AuraSource.None);
        Assert.Equal(AuraApplyResult.Stacked, _h.Auras.Apply(second, boar, new AuraId(901), AuraSource.None));
        Assert.Equal(AuraApplyResult.Stacked, _h.Auras.Apply(first, boar, new AuraId(901), AuraSource.None));

        ActiveAura bleed = Assert.Single(boar.Auras.All);
        Assert.Equal((3u, first.Guid), (bleed.Stacks, bleed.CasterGuid));
    }

    [Fact]
    public void Keep_one_copy_of_an_independent_aura_per_caster()
    {
        CharacterEntity first = _h.Player(909_105);
        CharacterEntity second = _h.Player(909_106);
        Creature boar = _h.Creature(909_904);

        _h.Auras.Apply(first, boar, new AuraId(906), AuraSource.None);
        _h.Auras.Apply(second, boar, new AuraId(906), AuraSource.None);

        Assert.Equal(2, boar.Auras.Count);
        Assert.Equal(AuraApplyResult.Refreshed, _h.Auras.Apply(first, boar, new AuraId(906), AuraSource.None));
        Assert.Equal(2, boar.Auras.Count);
    }

    /// <summary>A unit at the cap refuses a new aura and keeps every one it holds; a held one still refreshes.</summary>
    [Fact]
    public void Refuse_a_new_aura_past_the_cap_and_keep_the_others()
    {
        AuraTemplate[] many = Enumerable.Range(0, 33).Select(i => AuraTestData.Burn((uint)(1000 + i))).ToArray();
        _h.Use(many);
        CharacterEntity wizard = _h.Player(909_107);
        Creature boar = _h.Creature(909_905);

        for (int i = 0; i < 32; i++)
            Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(wizard, boar, many[i].Id, AuraSource.None));

        Assert.Equal(AuraApplyResult.Refused, _h.Auras.Apply(wizard, boar, many[32].Id, AuraSource.None));
        Assert.Equal(32, boar.Auras.Count);
        Assert.Equal(AuraApplyResult.Refreshed, _h.Auras.Apply(wizard, boar, many[0].Id, AuraSource.None));
    }

    /// <summary>At the cap, a stack aura the unit holds still stacks: the cap counts new auras only.</summary>
    [Fact]
    public void Stack_a_held_aura_at_the_cap()
    {
        var h = new AuraHarness(maxPerUnit: 1);
        h.Use(AuraTestData.Bleed(), AuraTestData.Burn());
        CharacterEntity warrior = h.Player(909_117);
        Creature boar = h.Creature(909_913);
        h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None);

        Assert.Equal(AuraApplyResult.Refused, h.Auras.Apply(warrior, boar, new AuraId(902), AuraSource.None));
        Assert.Equal(AuraApplyResult.Stacked, h.Auras.Apply(warrior, boar, new AuraId(901), AuraSource.None));
    }

    /// <summary>
    /// A duration given (a GameMaster's /aura) is placed exactly: on a creature immune to the stun, and on a character
    /// past its diminishing step, a refresh shortening what is held included; and it records no step, so the next stun
    /// without one lands at half, as the second.
    /// </summary>
    [Fact]
    public void Place_exactly_the_duration_given_past_resistance_and_diminishing()
    {
        AuraTemplate stun = AuraTestData.Stunned();
        _h.Use(stun);
        CharacterEntity warrior = _h.Player(909_130);
        CharacterEntity target = _h.Player(909_131);
        var boss = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, 909_930),
            Level = 1,
            Health = 1000,
            CurrentHealth = 1000,
            BaseMaxHealth = 1000,
            Speed = 4f,
            ControlResistance = new ControlResistance(0f, 100f, 100f, 100f),
        };
        _h.Creatures[boss.Guid] = boss;
        _h.Auras.Roster.Join(boss);

        Assert.Equal(AuraApplyResult.Resisted, _h.Auras.Apply(warrior, boss, stun, AuraSource.None));
        Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(warrior, boss, stun, AuraSource.None, durationMs: 4000));
        Assert.Equal(4000u, Assert.Single(boss.Auras.All).DurationMs);

        Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(warrior, target, stun, AuraSource.None));
        Assert.Equal(AuraApplyResult.Refreshed, _h.Auras.Apply(warrior, target, stun, AuraSource.None, durationMs: 400));
        ActiveAura held = Assert.Single(target.Auras.All);
        Assert.Equal((400u, 400u), (held.DurationMs, UnitAuras.RemainingMs(held, _h.Time.GetUtcNow())));

        _h.Advance(TimeSpan.FromSeconds(1));
        _h.Auras.Update();
        Assert.Empty(target.Auras.All);
        Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(warrior, target, stun, AuraSource.None));
        Assert.Equal(750u, Assert.Single(target.Auras.All).DurationMs);
    }

    [Fact]
    public void Refuse_an_aura_on_the_dead_and_a_harmful_one_on_a_target_that_ignores_hits()
    {
        CharacterEntity warrior = _h.Player(909_108);
        Creature corpse = _h.Creature(909_906, health: 0);
        Creature innkeeper = _h.Creature(909_907);
        innkeeper.Invulnerable = true;
        CharacterEntity dead = _h.Player(909_109);
        dead.IsDead = true;

        Assert.Equal(AuraApplyResult.Refused, _h.Auras.Apply(warrior, corpse, new AuraId(901), AuraSource.None));
        Assert.Equal(AuraApplyResult.Refused, _h.Auras.Apply(warrior, innkeeper, new AuraId(901), AuraSource.None));
        Assert.Equal(AuraApplyResult.Refused, _h.Auras.Apply(warrior, dead, new AuraId(904), AuraSource.None));
        Assert.Equal(AuraApplyResult.Refused, _h.Auras.Apply(warrior, _h.Creature(909_908), new AuraId(999), AuraSource.None));
        Assert.Null(_h.Combat.GetEncounterFor(innkeeper));
    }

    /// <summary>
    /// A reload can leave an ability naming an aura of the other kind: an Ally ability's harmful aura, or a Hostile
    /// one's helpful aura, applies nothing and forms no encounter.
    /// </summary>
    [Fact]
    public void Refuse_an_aura_that_no_longer_fits_the_ability_applying_it()
    {
        CharacterEntity healer = _h.Player(909_118);
        Creature boar = _h.Creature(909_914);
        AuraSource ally = AuraSource.None with { AbilityId = new AbilityId(233), Affects = AbilityAffects.Ally };
        AuraSource hostile = AuraSource.None with { AbilityId = new AbilityId(203), Affects = AbilityAffects.Hostile };

        Assert.Equal(AuraApplyResult.Refused, _h.Auras.Apply(healer, boar, new AuraId(901), ally));
        Assert.Equal(AuraApplyResult.Refused, _h.Auras.Apply(healer, healer, new AuraId(904), hostile));

        Assert.Equal(0, boar.Auras.Count);
        Assert.Equal(0, healer.Auras.Count);
        Assert.Null(_h.Combat.GetEncounterFor(boar));
        Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(healer, boar, new AuraId(901), hostile));
        Assert.Equal(AuraApplyResult.Applied, _h.Auras.Apply(healer, healer, new AuraId(904), ally));
    }

    [Fact]
    public void Enter_combat_on_a_harmful_aura_and_not_on_a_helpful_one()
    {
        CharacterEntity healer = _h.Player(909_110);
        CharacterEntity friend = _h.Player(909_111);
        Creature boar = _h.Creature(909_909);

        _h.Auras.Apply(healer, friend, new AuraId(904), AuraSource.None);
        Assert.False(healer.IsInCombat);

        _h.Auras.Apply(healer, boar, new AuraId(903), AuraSource.None);
        Assert.NotNull(_h.Combat.GetEncounterFor(boar));
        Assert.True(healer.IsInCombat);
    }

    [Fact]
    public void Refold_a_creatures_stats_as_a_modifier_aura_comes_and_goes()
    {
        CharacterEntity hunter = _h.Player(909_112);
        Creature boar = _h.Creature(909_910);

        _h.Auras.Apply(hunter, boar, new AuraId(903), AuraSource.None);
        Assert.Equal(0.7f, boar.SpeedFactor, precision: 4);

        _h.Auras.Remove(boar, Assert.Single(boar.Auras.All), AuraRemoveReason.Cancelled);
        Assert.Equal(1f, boar.SpeedFactor, precision: 4);
    }

    /// <summary>Crippled on a gearless warrior: its stats are refreshed over the reference data, 4 m/s to 2.8 and back.</summary>
    [Fact]
    public async Task Refresh_a_characters_stats_as_a_modifier_aura_comes_and_goes()
    {
        _h.Data = await TestStaticData.LoadAsync(classStats:
        [
            new ClassLevelStat
                { Class = CharacterClass.Warrior, Level = 1, BaseHp = 130, Stamina = 22, Strength = 23, Agility = 20, Intellect = 20 },
        ]);
        CharacterEntity warrior = _h.Player(909_119);
        Creature boar = _h.Creature(909_915);

        _h.Auras.Apply(boar, warrior, new AuraId(903), AuraSource.None);
        Assert.Equal(2.8f, warrior.GetMovementSpeed(), precision: 4);

        _h.Auras.Remove(warrior, Assert.Single(warrior.Auras.All), AuraRemoveReason.Expired);
        Assert.Equal(4f, warrior.GetMovementSpeed(), precision: 4);
    }

    [Fact]
    public void Remove_every_aura_or_only_the_harmful_ones()
    {
        CharacterEntity healer = _h.Player(909_113);
        CharacterEntity target = _h.Player(909_114);
        _h.Auras.Apply(healer, target, new AuraId(904), AuraSource.None);
        _h.Auras.Apply(healer, target, new AuraId(905), AuraSource.None);
        Creature boar = _h.Creature(909_911);
        _h.Auras.Apply(healer, boar, new AuraId(901), AuraSource.None);
        _h.Auras.Apply(healer, boar, new AuraId(902), AuraSource.None);

        _h.Auras.RemoveHarmful(boar, AuraRemoveReason.Reset);
        _h.Auras.RemoveAll(target, AuraRemoveReason.Death);

        Assert.Equal(0, boar.Auras.Count);
        Assert.Equal(0, target.Auras.Count);
        Assert.All(target.Auras.Changes.Where(c => c.Kind == AuraChangeKind.Removed), c => Assert.Equal(0u, c.RemainingMs));
    }

    /// <summary>
    /// The removal paths copy the auras they end into lists the system keeps (#883), and every copy the system made goes
    /// back to its pool when they end it (#1180 part A2), which the next pass opens to new placements. So once a round of
    /// the same work has warmed them, a round of it allocates nothing for auras with no script and no stat modifier:
    /// placing new copies on units that held some, the owner's cancel, the removal on death and the removal on a
    /// creature's reset. Nor does an application that is refused: an ability's aura on a corpse costs nothing before its
    /// refusal. The creatures' auras come from an ability shaped by capstones (#1181 Part D: 50 % longer, 2 stacks more),
    /// each applied twice, so the lengthened placement and the raised cap's stack cost nothing either. Every copy the round placed is one the round before ended, never one still held. Copies made elsewhere and
    /// ended here never join the pool, which stays at this system's own ten, each holding no template.
    /// </summary>
    [Fact]
    public void Place_refuse_and_end_auras_without_allocating()
    {
        CharacterEntity healer = _h.Player(909_120);
        CharacterEntity[] cancelling = [_h.Player(909_130), _h.Player(909_131)];
        Creature[] dying = [_h.Creature(909_930), _h.Creature(909_931)];
        Creature[] resetting = [_h.Creature(909_940), _h.Creature(909_941)];
        Creature corpse = _h.Creature(909_950);
        corpse.CurrentHealth = 0;
        AuraTemplate bleed = AuraTestData.Bleed();
        var fromAnAbility = new AuraSource(new AbilityId(203), 1f, 0f, AbilityAffects.Hostile);
        // A weaponless caster's Attack draws no roll for the aura's total, so the shaped source draws nothing either.
        var shaped = AuraSource.Of(Abilities.AbilityTestData.Shaped(Abilities.AbilityTestData.Cone(203),
            (TalentModifierKind.AuraStacks, 2, null), (TalentModifierKind.AuraDurationPct, 50, null)));
        var renew = new AuraId(904);
        AuraId[] harmful = [new AuraId(901), new AuraId(902)];
        Creature[] creatures = [.. dying, .. resetting];
        IUnit[] holders = [.. cancelling, .. creatures];

        // A visitor, as in a town, ends copies made elsewhere every round: two by another instance's aura system and one
        // restored at login. They are left to the collector, so this pool holds only the copies it made.
        var elsewhere = new AuraHarness();
        elsewhere.Use(AuraTestData.Bleed(), AuraTestData.Burn());
        Creature visitor = _h.Creature(909_960);

        // The copies the last round ended, by reference, against which the next round's placements are checked.
        HashSet<ActiveAura> ended = new(ReferenceEqualityComparer.Instance);
        long allocated = 0;
        AuraApplyResult refused = default;
        (AuraCancelResult First, AuraCancelResult Second) cancelled = default;
        for (int round = 0; round < 2; round++)
        {
            // Between rounds a pass starts, which pools what the last round ended, and the broadcast clears the changes.
            _h.Auras.Update();
            _h.Auras.Roster.ClearChanges();
            for (int a = 0; a < harmful.Length; a++)
                elsewhere.Auras.Apply(null, visitor, harmful[a], AuraSource.None);
            visitor.Auras.Add(new ActiveAura(AuraTestData.Fortified(), new ObjectGuid(), AuraSource.None, 1, default,
                AuraSchedule.Start(AuraHarness.T0, 60_000, 0), 60_000, AuraHarness.T0.UtcDateTime), AuraHarness.T0);
            visitor.Auras.ClearChanges();

            long before = ThreadAllocations.BeforeQuietWindow();
            for (int i = 0; i < cancelling.Length; i++)
                _h.Auras.Apply(healer, cancelling[i], renew, AuraSource.None);
            for (int i = 0; i < creatures.Length; i++)
            {
                for (int a = 0; a < harmful.Length; a++)
                {
                    _h.Auras.Apply(healer, creatures[i], harmful[a], shaped);
                    _h.Auras.Apply(healer, creatures[i], harmful[a], shaped);
                }
            }

            long placing = GC.GetAllocatedBytesForCurrentThread() - before;
            ActiveAura shapedBleed = creatures[0].Auras.All[0];
            Assert.Equal((901u, 2u, 18_000u), (shapedBleed.Id.Value, shapedBleed.Stacks, shapedBleed.DurationMs));

            // Outside the measure: the round's copies against the last round's.
            if (round == 1)
                Assert.All(holders, unit => Assert.All(AuraHolders.Of(unit)!.All, held => Assert.Contains(held, ended)));
            ended.Clear();
            foreach (IUnit unit in holders)
                ended.UnionWith(AuraHolders.Of(unit)!.All);
            Assert.Equal(10, ended.Count);

            before = ThreadAllocations.BeforeQuietWindow();
            refused = _h.Auras.Apply(healer, corpse, bleed, fromAnAbility);
            cancelled = (_h.Auras.Cancel(cancelling[0], renew),
                _h.Auras.Cancel(cancelling[1], renew, cancelling[1].Auras.All[0].Key));
            for (int i = 0; i < dying.Length; i++)
                _h.Auras.RemoveAll(dying[i], AuraRemoveReason.Death);
            for (int i = 0; i < resetting.Length; i++)
                _h.Auras.RemoveHarmful(resetting[i], AuraRemoveReason.Reset);
            _h.Auras.RemoveAll(visitor, AuraRemoveReason.Expired);
            allocated = placing + GC.GetAllocatedBytesForCurrentThread() - before;
        }

        // The pool, once the last round's copies joined it: this instance's ten, holding nothing they last referenced.
        _h.Auras.Update();
        var pool = (Stack<ActiveAura>)typeof(AuraSystem).GetField("_free", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_h.Auras)!;
        Assert.Equal(10, pool.Count);
        Assert.All(pool, copy => Assert.Null(copy.Template));
        Assert.Equal(0, visitor.Auras.Count);
        Assert.Equal(0, allocated);
        Assert.Equal(AuraApplyResult.Refused, refused);
        Assert.Equal((AuraCancelResult.Ok, AuraCancelResult.Ok), cancelled);
        Assert.All(cancelling, character => Assert.Equal(0, character.Auras.Count));
        Assert.All(dying.Concat(resetting), creature => Assert.Equal(0, creature.Auras.Count));
    }
}

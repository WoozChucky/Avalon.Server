using Avalon.Combat;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abilities;
using Avalon.World;
using Avalon.World.Abilities;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Testing;
using Avalon.World.Testing.Scenarios;

namespace Avalon.Server.World.UnitTests.Performance;

/// <summary>
/// An ability cast allocates nothing once warm: a projectile (#1181 Part D, the owner's decision of 2026-10-28), a cone and
/// a circle (#1238). The instance's pool hands the script, a projectile's hit set and the cast system's entries out again,
/// instant or queued, and the dodge wait holds a telegraph's hit on a player in a copy of the ability its list reuses.
/// Through the real cast path, with nothing substituted, since a substitute allocates on every call: a real instance over
/// the town's navmesh, its real script manager, and characters behind real connections, so their views are decided and
/// their clients sent every add, update, remove, fired, impact and damage the casts make, and the view rounds the pool
/// waits on pass. The boars have no script, so nothing but the casts moves in the instance.
/// </summary>
/// <remarks>
/// Every round sends, so this runs where <see cref="InventoryAllocationShould" /> does and reads the fewest of three
/// windows, for the reason given there.
/// </remarks>
[Collection(nameof(ScenarioAllocations))]
public class AbilityCastAllocationShould
{
    private const int Rounds = 20;
    private const float Distance = 6f;
    private const uint CreatureHealth = 100_000_000;

    // The Wizard's Spark Bolt (a projectile), Flame Burst (a circle on the aim point) and Frost Fan (a 60° cone, 6 m), the
    // seed's Bramble Nova (a creature's 6 m circle on itself, wound up 1.2 s) and its Thornback Boar.
    private static readonly AbilityId s_sparkBolt = new(210);
    private static readonly AbilityId s_flameBurst = new(211);
    private static readonly AbilityId s_frostFan = new(212);
    private static readonly AbilityId s_brambleNova = new(315);
    private static readonly CreatureTemplateId s_boar = new(4);

    /// <summary>
    /// A Wizard casts at three boars 6 m north, at 0° and ±10° from the aim. A projectile strikes the centre boar alone; a
    /// fan of three (<c>AddedProjectiles</c> 2) takes each of its lanes from the pool and strikes every boar, a lane each; a
    /// cone or a circle strikes all three at once, and its script, which finishes as it fires, goes back to the pool then.
    /// </summary>
    [Theory]
    [InlineData("instant")]
    [InlineData("queued")]
    [InlineData("instant fan of three")]
    [InlineData("queued fan of three")]
    [InlineData("instant cone")]
    [InlineData("queued cone")]
    [InlineData("instant circle")]
    [InlineData("queued circle")]
    public void Cast_an_ability_allocating_nothing_once_warm(string path)
    {
        using var world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        MapInstance instance = world.AddInstance(MapType.Normal);
        CharacterEntity wizard = world.NewCharacter(CharacterClass.Wizard);
        wizard.GodMode = true;
        Vector3 at = TownNavmesh.EntrySpawn;
        world.Join(instance, wizard, at);
        bool fan = path.EndsWith("fan of three", StringComparison.Ordinal);
        if (fan)
            ShapeAFan(wizard, world.Host.Data);
        IAbility ability = path.EndsWith("cone", StringComparison.Ordinal) ? Ability(world.Host.Data, s_frostFan)
            : path.EndsWith("circle", StringComparison.Ordinal) ? Ability(world.Host.Data, s_flameBurst)
            : wizard.Spells[s_sparkBolt]!;
        AbilityMetadata meta = ability.Metadata;
        bool projectile = meta.Shape == AbilityShape.Projectile;
        Assert.Equal(fan ? 3 : 1, meta.ProjectileCount);

        // The lane passes the side boars further off than a body; a cone's arc and a circle's radius take them in.
        CreatureTemplate template = world.Host.Data.CreatureTemplates.Single(t => t.Id == s_boar);
        Creature centre = Boar(instance, template, at, 0f);
        Creature left = Boar(instance, template, at, -10f);
        Creature right = Boar(instance, template, at, 10f);
        Assert.True(Distance * MathF.Sin(10f * MathF.PI / 180f) > left.BodyRadius + 0.1f);
        var aim = new AbilityAim(new Vector3(0f, 0f, 1f), centre.Position);

        // A round covers the cast time, a projectile's flight and its final state's broadcast, and the two view rounds the
        // pool waits for: each broadcast period is a view round.
        int ticks = (int)MathF.Ceiling(meta.CastTime * 60f) + 3 * BroadcastSchedule.Slots + 2
                    + (projectile ? (int)MathF.Ceiling(Distance / meta.ProjectileSpeed * 60f) : 0);
        bool queued = path.StartsWith("queued", StringComparison.Ordinal);
        int taken = 0;
        int casts = 0;

        // A creature with no script takes no health off a hit (its script does, Creature.OnHit), so a hit is read off each
        // struck boar's encounter, which every landed hit moves on (Encounter.AddThreat): its last damage time, read at the
        // end of each window. Read off one encounter per boar, held from the warm-up on, so one that ended and started again
        // fails too.
        Creature[] struck = fan || !projectile ? [left, centre, right] : [centre];
        IEncounter[]? fights = null;
        var struckBefore = new DateTime[struck.Length];
        int[] windowsStruck = new int[struck.Length];

        void Round()
        {
            casts++;
            if (queued ? instance.QueueAbility(wizard, aim, ability) : instance.RunInstantAbility(wizard, aim, ability))
                taken++;
            for (int i = 0; i < ticks; i++)
                world.Tick();

            if (fights is not null && casts % Rounds == 0)
            {
                for (int b = 0; b < fights.Length; b++)
                {
                    if (fights[b].LastDamageTime > struckBefore[b])
                        windowsStruck[b]++;
                    struckBefore[b] = fights[b].LastDamageTime;
                }
            }
        }

        for (int i = 0; i < Rounds; i++)
            Round();
        fights = [.. struck.Select(b => instance.CombatService.GetEncounterFor(b)!)];
        Assert.All(fights, Assert.NotNull);
        for (int b = 0; b < fights.Length; b++)
            struckBefore[b] = fights[b].LastDamageTime;

        long bytes = ThreadAllocations.FewestOfThreeWindows(Rounds, Round);

        Assert.Equal(0, bytes);
        Assert.Equal(casts, taken);
        Assert.All(windowsStruck, windows => Assert.Equal(casts / Rounds - 1, windows));
        for (int b = 0; b < struck.Length; b++)
            Assert.Same(fights[b], instance.CombatService.GetEncounterFor(struck[b]));
        if (struck.Length == 1)
        {
            Assert.Null(instance.CombatService.GetEncounterFor(left));
            Assert.Null(instance.CombatService.GetEncounterFor(right));
        }
    }

    /// <summary>
    /// A boar's telegraphed circle, Bramble Nova, on a player 2 m off allocates nothing once warm either (#1238): the dodge
    /// wait holds its hit for the player's one-way delay (the scenario's 60 ms round trip: 2 ticks) in a copy of the
    /// ability its list takes from its free ones, and lands it after the circle's script has gone back to the pool, so the
    /// hit reads its own copy, never the script's, which the pool cleared. Every hit that lands, lands 2 ticks after the
    /// circle fired, never on the fire tick.
    /// </summary>
    [Fact]
    public void Hold_and_land_a_telegraphs_hit_allocating_nothing_once_warm()
    {
        using var world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        MapInstance instance = world.AddInstance(MapType.Normal);
        CharacterEntity player = world.NewCharacter(1238);
        Vector3 at = TownNavmesh.EntrySpawn;
        world.Join(instance, player, at);
        CreatureTemplate template = world.Host.Data.CreatureTemplates.Single(t => t.Id == s_boar);
        Creature boar = Boar(instance, template, at, 0f, distance: 2f);
        GameAbility nova = Ability(world.Host.Data, s_brambleNova);
        var aim = new AbilityAim(new Vector3(0f, 0f, -1f), null);
        int ticks = (int)MathF.Ceiling(nova.Metadata.CastTime * 60f) + 3 * BroadcastSchedule.Slots + 2;
        int taken = 0;
        int casts = 0;
        int landedOnFire = 0;
        int landedLate = 0;
        int landedElse = 0;
        int windowsStruck = 0;
        uint healthBefore = player.CurrentHealth;

        void Round()
        {
            casts++;
            if (instance.QueueAbility(boar, aim, nova))
                taken++;

            int fired = -1;
            for (int i = 0; i < ticks; i++)
            {
                uint health = player.CurrentHealth;
                world.Tick();
                if (fired < 0 && !nova.Casting)
                    fired = i;
                if (player.CurrentHealth >= health)
                    continue;
                if (i == fired)
                    landedOnFire++;
                else if (i == fired + 2)
                    landedLate++;
                else
                    landedElse++;
            }

            if (casts > Rounds && casts % Rounds == 0)
            {
                if (player.CurrentHealth < healthBefore)
                    windowsStruck++;
                healthBefore = player.CurrentHealth;
            }
        }

        for (int i = 0; i < Rounds; i++)
            Round();
        healthBefore = player.CurrentHealth;

        long bytes = ThreadAllocations.FewestOfThreeWindows(Rounds, Round);

        Assert.Equal(0, bytes);
        Assert.Equal(casts, taken);
        Assert.Equal(casts / Rounds - 1, windowsStruck);
        Assert.True(landedLate > 0);
        Assert.Equal((0, 0), (landedOnFire, landedElse));
        Assert.False(player.IsDead);
    }

    /// <summary>A seeded ability as a character's list holds it: its row through the production mapper, ready to cast.</summary>
    private static GameAbility Ability(StaticData data, AbilityId id)
    {
        Assert.True(data.Abilities.TryGet(id, out AbilityTemplate? template));
        return new GameAbility
        {
            AbilityId = id,
            Metadata = AbilityMetadataMapper.From(template!),
            CastTimeTimer = template!.CastTime / 1000f,
            CooldownTimer = 0f,
        };
    }

    /// <summary>
    /// The Wizard's Spark Bolt replaced in its list by a plain ability shaped as a fan of three (<c>AddedProjectiles</c> 2),
    /// as a held capstone shapes it (<see cref="AbilityShaping.Apply" />).
    /// </summary>
    private static void ShapeAFan(CharacterEntity wizard, StaticData data)
    {
        Assert.True(data.Abilities.TryGet(s_sparkBolt, out AbilityTemplate? template));
        AbilityShaping shaping = default;
        shaping.Add(TalentModifierKind.AddedProjectiles, 2, null);
        var shaped = new GameAbility
        {
            AbilityId = s_sparkBolt,
            Metadata = shaping.Apply(AbilityMetadataMapper.From(template!)),
            CastTimeTimer = template!.CastTime / 1000f,
            CooldownTimer = 0f,
        };
        wizard.Spells.Load([.. wizard.Spells.All.Select(a => a.AbilityId == s_sparkBolt ? shaped : a)]);
    }

    /// <summary>
    /// A boar of the seed's template with no script, <paramref name="distance" /> from <paramref name="from" /> at
    /// <paramref name="degrees" /> from north.
    /// </summary>
    private static Creature Boar(MapInstance instance, CreatureTemplate template, Vector3 from, float degrees,
        float distance = Distance)
    {
        float radians = degrees * MathF.PI / 180f;
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, IObject.GenerateId()),
            TemplateId = template.Id,
            Metadata = template,
            Name = template.Name,
            Position = new Vector3(from.x + distance * MathF.Sin(radians), from.y, from.z + distance * MathF.Cos(radians)),
            BodyRadius = template.BodyRadius,
            Level = 1,
            Health = CreatureHealth,
            CurrentHealth = CreatureHealth,
        };
        instance.AddCreature(boar);
        return boar;
    }
}

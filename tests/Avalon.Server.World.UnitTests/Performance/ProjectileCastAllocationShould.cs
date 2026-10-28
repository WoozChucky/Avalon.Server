using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
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
/// A projectile cast allocates nothing once warm (#1181 Part D, the owner's decision of 2026-10-28): the instance's pool
/// hands a projectile, its hit set and the cast system's entries out again, instant or queued. Through the real cast
/// path, with nothing substituted, since a substitute allocates on every call: a real instance over the town's navmesh, its
/// real script manager, and a Wizard behind a real connection, so its view is decided and its client sent every add,
/// update, remove, fired, impact and damage the flight makes, and the view rounds the pool waits on pass. The boars have
/// no script, so nothing but the casts moves in the instance.
/// </summary>
/// <remarks>
/// Every round sends, so this runs where <see cref="InventoryAllocationShould" /> does and reads the fewest of three
/// windows, for the reason given there.
/// </remarks>
[Collection(nameof(ScenarioAllocations))]
public class ProjectileCastAllocationShould
{
    private const int Rounds = 20;
    private const float Distance = 6f;
    private const uint CreatureHealth = 100_000_000;

    // Spark Bolt, the Wizard's projectile basic, and the seed's Thornback Boar.
    private static readonly AbilityId s_sparkBolt = new(210);
    private static readonly CreatureTemplateId s_boar = new(4);

    [Theory]
    [InlineData("instant")]
    [InlineData("queued")]
    public void Cast_and_fly_a_projectile_allocating_nothing_once_warm(string path)
    {
        using var world = ScenarioWorld.CreateWithReferenceData(seed: 1);
        MapInstance instance = world.AddInstance(MapType.Normal);
        CharacterEntity wizard = world.NewCharacter(CharacterClass.Wizard);
        wizard.GodMode = true;
        Vector3 at = TownNavmesh.EntrySpawn;
        world.Join(instance, wizard, at);
        IAbility sparkBolt = wizard.Spells[s_sparkBolt]!;

        // Three boars 6 m out, at 0° and ±10° from the aim, north: the lane passes the side ones further off than a body.
        CreatureTemplate template = world.Host.Data.CreatureTemplates.Single(t => t.Id == s_boar);
        Creature centre = Boar(instance, template, at, 0f);
        Creature left = Boar(instance, template, at, -10f);
        Creature right = Boar(instance, template, at, 10f);
        Assert.True(Distance * MathF.Sin(10f * MathF.PI / 180f) > left.BodyRadius + 0.1f);
        var aim = new AbilityAim(new Vector3(0f, 0f, 1f), centre.Position);

        // A round covers the cast time, the flight, the final state's broadcast, and the two view rounds the pool waits
        // for: each broadcast period is a view round.
        AbilityMetadata meta = sparkBolt.Metadata;
        int ticks = (int)MathF.Ceiling(meta.CastTime * 60f) + (int)MathF.Ceiling(Distance / meta.ProjectileSpeed * 60f)
                    + 3 * BroadcastSchedule.Slots + 2;
        bool queued = path == "queued";
        int taken = 0;
        int casts = 0;

        // A creature with no script takes no health off a hit (its script does, Creature.OnHit), so a hit is read off the
        // centre's encounter, which every landed hit moves on (Encounter.AddThreat): its last damage time, read at the end
        // of each window. Read off one encounter, held from the warm-up on, so one that ended and started again fails too.
        IEncounter? fight = null;
        DateTime struckBefore = default;
        int windowsStruck = 0;

        void Round()
        {
            casts++;
            if (queued ? instance.QueueAbility(wizard, aim, sparkBolt) : instance.RunInstantAbility(wizard, aim, sparkBolt))
                taken++;
            for (int i = 0; i < ticks; i++)
                world.Tick();

            if (fight is not null && casts % Rounds == 0)
            {
                if (fight.LastDamageTime > struckBefore)
                    windowsStruck++;
                struckBefore = fight.LastDamageTime;
            }
        }

        for (int i = 0; i < Rounds; i++)
            Round();
        fight = instance.CombatService.GetEncounterFor(centre);
        Assert.NotNull(fight);
        struckBefore = fight.LastDamageTime;

        long bytes = ThreadAllocations.FewestOfThreeWindows(Rounds, Round);

        Assert.Equal(0, bytes);
        Assert.Equal(casts, taken);
        Assert.Equal(casts / Rounds - 1, windowsStruck);
        Assert.Same(fight, instance.CombatService.GetEncounterFor(centre));
        Assert.Null(instance.CombatService.GetEncounterFor(left));
        Assert.Null(instance.CombatService.GetEncounterFor(right));
    }

    /// <summary>A boar of the seed's template with no script, <see cref="Distance" /> from <paramref name="from" />.</summary>
    private static Creature Boar(MapInstance instance, CreatureTemplate template, Vector3 from, float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        var boar = new Creature
        {
            Guid = new ObjectGuid(ObjectType.Creature, IObject.GenerateId()),
            TemplateId = template.Id,
            Metadata = template,
            Name = template.Name,
            Position = new Vector3(from.x + Distance * MathF.Sin(radians), from.y, from.z + Distance * MathF.Cos(radians)),
            BodyRadius = template.BodyRadius,
            Level = 1,
            Health = CreatureHealth,
            CurrentHealth = CreatureHealth,
        };
        instance.AddCreature(boar);
        return boar;
    }
}

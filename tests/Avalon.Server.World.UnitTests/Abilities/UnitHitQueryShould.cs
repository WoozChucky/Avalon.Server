using System.Reflection;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Abilities.Targeting;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Instances.MapInstanceClients;

namespace Avalon.Server.World.UnitTests.Abilities;

public class UnitHitQueryShould
{
    private readonly Dictionary<ObjectGuid, ICharacter> _characters = new();
    private readonly Dictionary<ObjectGuid, ICreature> _creatures = new();

    private UnitHitQuery Query => new(_characters, _creatures);

    private ICreature Creature(uint id, float x, float z, float body = 0.5f, uint health = 10)
    {
        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, id));
        creature.Position.Returns(new Vector3(x, 0f, z));
        creature.BodyRadius.Returns(body);
        creature.CurrentHealth.Returns(health);
        _creatures[creature.Guid] = creature;
        return creature;
    }

    private ICharacter Character(uint id, float x, float z, bool dead = false, uint health = 10)
    {
        ICharacter character = Substitute.For<ICharacter>();
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        character.Position.Returns(new Vector3(x, 0f, z));
        character.BodyRadius.Returns(0.5f);
        character.CurrentHealth.Returns(health);
        character.IsDead.Returns(dead);
        _characters[character.Guid] = character;
        return character;
    }

    [Fact]
    public void Return_characters_and_creatures_nearest_first()
    {
        ICreature far = Creature(1, 0f, 2.5f);
        ICharacter near = Character(2, 0f, 1f);
        ICreature middle = Creature(3, 0f, 2f);

        Assert.Equal([near, middle, far], Query.InCircle(Vector3.zero, 3f));
    }

    [Fact]
    public void Break_distance_ties_by_guid()
    {
        ICreature second = Creature(9, 1f, 0f);
        ICreature first = Creature(4, -1f, 0f);

        Assert.Equal([first, second], Query.InCircle(Vector3.zero, 3f));
    }

    [Fact]
    public void Leave_out_dead_units()
    {
        Character(1, 0f, 1f, dead: true, health: 10);
        Creature(2, 0f, 1f, health: 0);   // a corpse stays in the instance until the corpse remover takes it

        Assert.Empty(Query.InCircle(Vector3.zero, 3f));
    }

    [Fact]
    public void Judge_a_character_alive_by_its_dead_flag_not_its_health()
    {
        ICharacter alive = Character(1, 0f, 1f, dead: false, health: 0);

        Assert.Equal([alive], Query.InCircle(Vector3.zero, 3f));
    }

    [Fact]
    public void Use_each_units_own_body_radius()
    {
        ICreature big = Creature(1, 4f, 0f, body: 2f);
        Creature(2, 4f, 0.1f, body: 0.5f);

        Assert.Equal([big], Query.InCircle(Vector3.zero, 2.5f));
    }

    [Fact]
    public void Order_a_segment_from_its_start()
    {
        ICreature later = Creature(1, 0f, 8f);
        ICreature sooner = Creature(2, 0f, 3f);

        Assert.Equal([sooner, later], Query.OnSegment(Vector3.zero, new Vector3(0f, 0f, 10f)));
    }

    [Fact]
    public void Keep_a_result_intact_while_a_second_query_runs_before_the_next_tick()
    {
        ICreature near = Creature(1, 0f, 1f);
        ICreature far = Creature(2, 0f, 8f);
        var query = new UnitHitQuery(_characters, _creatures);

        IReadOnlyList<IUnit> circle = query.InCircle(Vector3.zero, 3f);
        IReadOnlyList<IUnit> segment = query.OnSegment(Vector3.zero, new Vector3(0f, 0f, 10f));

        Assert.NotSame(circle, segment);
        Assert.Equal([near], circle);
        Assert.Equal([near, far], segment);
    }

    [Fact]
    public void Order_a_cone_from_its_apex()
    {
        ICreature later = Creature(1, 0f, 2.5f);
        ICreature sooner = Creature(2, 0.3f, 1f);

        Assert.Equal([sooner, later], Query.InCone(Vector3.zero, new Vector3(0f, 0f, 1f), 3f, 90f));
    }

    /// <summary>
    /// #982: in an instance, a query visits the spatial grid's cells around its shape, and must find the hits a scan of
    /// every member finds, in the same order. Two instances hold the same units side by side, one asking over the grid
    /// and one scanning (<c>MapInstance.FindHitsByFullScan</c>), and each cast is asked of both. The units are what the
    /// grid can get wrong: bodies from 0.3 m to 12 m (the box is widened by the largest the grid indexed), on cell edges,
    /// off the layout, with a component of their position not finite (in no cell; one with only its height not finite
    /// can still be hit), dead, a test's doubles and a character also in another instance (neither indexed here). Most
    /// casts touch a unit's body at its edge, from any side; the rest are anywhere, with sizes up to infinite and origins
    /// that are not finite. Ids 982_2xx are unique to this test.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Find_over_the_spatial_grid_the_hits_a_scan_of_every_member_finds(int seed)
    {
        var random = new Random(seed);
        ChunkLayout layout = GridLayout();
        using MapInstance indexed = TestMapInstances.Build(NewWorld(), layout: layout);
        using MapInstance scanned = TestMapInstances.Build(NewWorld(), layout: layout);
        typeof(MapInstance).GetMethod("FindHitsByFullScan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scanned, null);
        using MapInstance indexedElsewhere = TestMapInstances.Build(NewWorld(), layout: layout);
        using MapInstance scannedElsewhere = TestMapInstances.Build(NewWorld(), layout: layout);

        var units = new List<Twin>();
        for (uint id = 982_201; id <= 982_206; id++)
        {
            CharacterEntity a = TestCharacters.New(id), b = TestCharacters.New(id);
            Join(indexed, a);
            Join(scanned, b);
            if (id == 982_206)
            {
                // Also in another instance, whose grid indexes it from then on.
                Join(indexedElsewhere, a);
                Join(scannedElsewhere, b);
            }

            units.Add(new Twin(a, b));
        }

        float[] bodies = [0.3f, 0.5f, 1.5f, 4f, 9.5f];
        for (uint id = 982_210; id < 982_260; id++)
        {
            float body = bodies[random.Next(bodies.Length)];
            Creature a = NewCreature(id, body), b = NewCreature(id, body);
            indexed.AddCreature(a);
            scanned.AddCreature(b);
            units.Add(new Twin(a, b));
        }

        ICharacter character = Substitute.For<ICharacter>();
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, 982_290));
        character.BodyRadius.Returns(0.5f);
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        indexed.AddCharacter(connection);
        scanned.AddCharacter(connection);
        units.Add(new Twin(character, character));

        ICreature creature = Substitute.For<ICreature>();
        creature.Guid.Returns(new ObjectGuid(ObjectType.Creature, 982_291));
        creature.BodyRadius.Returns(12f);
        creature.CurrentHealth.Returns(10u);
        indexed.AddCreature(creature);
        scanned.AddCreature(creature);
        units.Add(new Twin(creature, creature));

        foreach (Twin unit in units)
            Place(unit, Anywhere(random));

        var indexedHits = (UnitHitQuery)indexed.Hits;
        var scannedHits = (UnitHitQuery)scanned.Hits;
        for (int round = 0; round < 40; round++)
        {
            foreach (Twin unit in units)
            {
                if (random.Next(3) == 0)
                    Place(unit, Anywhere(random));
                if (random.Next(20) == 0)
                    Kill(unit, random.Next(2) == 0);
            }

            for (int cast = 0; cast < 60; cast++)
            {
                Twin target = units[random.Next(units.Count)];
                int shape = random.Next(3);
                IReadOnlyList<IUnit> overGrid, byScan;
                switch (shape)
                {
                    case 0:
                    {
                        float radius = Size(random);
                        Vector3 centre = Origin(random, target, radius);
                        overGrid = indexedHits.InCircle(centre, radius);
                        byScan = scannedHits.InCircle(centre, radius);
                        break;
                    }
                    case 1:
                    {
                        float reach = Size(random);
                        Vector3 apex = Origin(random, target, reach);
                        Vector3 direction = Direction(random, apex, target.Indexed.Position);
                        float arc = random.Next(5) switch { 0 => 10f, 1 => 60f, 2 => 90f, 3 => 180f, _ => 360f };
                        overGrid = indexedHits.InCone(apex, direction, reach, arc);
                        byScan = scannedHits.InCone(apex, direction, reach, arc);
                        break;
                    }
                    default:
                    {
                        (Vector3 from, Vector3 to) = Segment(random, target);
                        overGrid = indexedHits.OnSegment(from, to);
                        byScan = scannedHits.OnSegment(from, to);
                        break;
                    }
                }

                Assert.True(byScan.Select(u => u.Guid.RawValue).SequenceEqual(overGrid.Select(u => u.Guid.RawValue)),
                    $"seed {seed}, round {round}, cast {cast} (shape {shape}): the scan hit " +
                    $"[{string.Join(", ", byScan.Select(u => u.Guid.Id))}], the grid " +
                    $"[{string.Join(", ", overGrid.Select(u => u.Guid.Id))}]");
            }

            indexedHits.Recycle();
            scannedHits.Recycle();
        }
    }

    private const float GridCell = 30f;

    private static Creature NewCreature(uint id, float body) => new()
    {
        Guid = new ObjectGuid(ObjectType.Creature, id),
        Metadata = Loot.LootTestData.BoarTemplate(null),
        Level = 1,
        Health = 100,
        CurrentHealth = 100,
        BodyRadius = body,
    };

    // A double is the same object on both sides, its position read live.
    private static void Place(Twin unit, Vector3 position)
    {
        if (ReferenceEquals(unit.Indexed, unit.Scanned))
        {
            unit.Indexed.Position.Returns(position);
            return;
        }

        unit.Indexed.Position = position;
        unit.Scanned.Position = position;
    }

    private static void Kill(Twin unit, bool dead)
    {
        foreach (IUnit side in new[] { unit.Indexed, unit.Scanned })
        {
            switch (side)
            {
                case CharacterEntity entity:
                    entity.IsDead = dead;
                    break;
                case Creature creature:
                    creature.CurrentHealth = dead ? 0u : 100u;
                    break;
            }
        }
    }

    // On the layout, on a cell edge or a float either side of it, off the layout, or with a component not finite.
    private static Vector3 Anywhere(Random random)
    {
        float y = (float)(random.NextDouble() * 4);
        switch (random.Next(8))
        {
            case 0:
                return new Vector3(Edge(random, random.Next(-1, 9)), y, Edge(random, random.Next(-3, 6)));
            case 1:
                return new Vector3(Edge(random, random.Next(-1, 9)), y, (float)(random.NextDouble() * 220 - 100));
            case 2:
                return random.Next(3) switch
                {
                    0 => new Vector3(-400f + Offset(random), y, 50f + Offset(random)),
                    1 => new Vector3(900f + Offset(random), y, -300f + Offset(random)),
                    _ => new Vector3(1e6f, y, -1e6f),
                };
            case 3:
            {
                float bad = random.Next(3) switch { 0 => float.NaN, 1 => float.PositiveInfinity, _ => float.NegativeInfinity };
                return random.Next(3) switch
                {
                    0 => new Vector3(bad, y, 10f),
                    1 => new Vector3(10f + Offset(random), bad, 10f + Offset(random)),
                    _ => new Vector3(10f, y, bad),
                };
            }
            default:
                return new Vector3((float)(random.NextDouble() * 290 - 40), y, (float)(random.NextDouble() * 220 - 100));
        }
    }

    private static float Edge(Random random, int line) => random.Next(3) switch
    {
        0 => line * GridCell,
        1 => MathF.BitDecrement(line * GridCell),
        _ => MathF.BitIncrement(line * GridCell),
    };

    private static float Offset(Random random) => (float)(random.NextDouble() * 40 - 20);

    private static float Size(Random random) => random.Next(20) switch
    {
        0 => 0f,
        1 => -3f,
        2 => float.NaN,
        3 => float.PositiveInfinity,
        4 => 1e6f,
        5 => 300f,
        6 or 7 => 31f,
        _ => (float)(random.NextDouble() * 15),
    };

    // Mostly where the shape's edge meets the target's body, a float either side or a little inside, from any side; the
    // rest anywhere, or not finite.
    private static Vector3 Origin(Random random, Twin target, float size)
    {
        switch (random.Next(10))
        {
            case 0:
                return Anywhere(random);
            case 1:
                return random.Next(3) switch
                {
                    0 => new Vector3(float.NaN, 0f, 5f),
                    1 => new Vector3(5f, 0f, float.PositiveInfinity),
                    _ => new Vector3(-3e30f, 0f, 3e30f),
                };
            default:
            {
                float reach = MathF.Abs(size) + target.Indexed.BodyRadius;
                reach = random.Next(4) switch
                {
                    0 => MathF.BitDecrement(reach),
                    1 => MathF.BitIncrement(reach),
                    2 => reach * 0.98f,
                    _ => reach,
                };
                Vector3 at = target.Indexed.Position;
                double angle = random.Next(2) == 0 ? random.Next(8) * Math.PI / 4 : random.NextDouble() * 2 * Math.PI;
                return new Vector3(at.x + reach * (float)Math.Cos(angle), at.y, at.z + reach * (float)Math.Sin(angle));
            }
        }
    }

    // Toward the target mostly; otherwise any way, or not finite.
    private static Vector3 Direction(Random random, Vector3 apex, Vector3 target)
    {
        double angle = random.Next(4) switch
        {
            0 => random.NextDouble() * 2 * Math.PI,
            1 => double.NaN,
            _ => Math.Atan2((double)target.z - apex.z, (double)target.x - apex.x),
        };
        return new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
    }

    // Mostly passing the target's body at its edge, a float either side or a little inside; else a point, or anywhere.
    private static (Vector3 From, Vector3 To) Segment(Random random, Twin target)
    {
        float length = random.Next(6) switch { 0 => 0f, 1 => 1f, 2 => 5f, 3 => 20f, 4 => 45f, _ => 200f };
        double angle = random.Next(2) == 0 ? random.Next(8) * Math.PI / 4 : random.NextDouble() * 2 * Math.PI;
        var along = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
        if (random.Next(8) == 0)
        {
            Vector3 start = Anywhere(random);
            return (start, start + along * length);
        }

        float body = target.Indexed.BodyRadius;
        float aside = random.Next(4) switch
        {
            0 => MathF.BitDecrement(body),
            1 => MathF.BitIncrement(body),
            2 => body * 0.98f,
            _ => body,
        };
        Vector3 at = target.Indexed.Position;
        var passing = new Vector3(at.x - along.z * aside, at.y, at.z + along.x * aside);
        float before = (float)(random.NextDouble() * length);
        return (passing - along * before, passing + along * (length - before));
    }

    // A forest-like layout: a path of 30 m chunks with a branch, so most of the grid's cells are off the layout.
    private static ChunkLayout GridLayout()
    {
        (short X, short Z)[] cells =
            [(0, 0), (1, 0), (2, 0), (2, 1), (3, 1), (4, 1), (4, 2), (4, 3), (5, 3), (6, 3), (1, -1), (1, -2)];
        var chunks = cells
            .Select(c => new PlacedChunk(new ChunkTemplateId(1), c.X, c.Z, 0, new Vector3(c.X * GridCell, 0f, c.Z * GridCell)))
            .ToList();
        return new ChunkLayout(0, chunks, chunks[0], null, [], Vector3.zero, GridCell);
    }

    /// <summary>One unit on each side: in the instance over the grid and in the one that scans; a double is in both.</summary>
    private sealed record Twin(IUnit Indexed, IUnit Scanned);
}

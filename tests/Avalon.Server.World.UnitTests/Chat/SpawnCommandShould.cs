using System.Globalization;
using Avalon.Combat;
using Avalon.Common.Accounts;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.ChunkLayouts;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public;
using Avalon.World.Public.Creatures;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Chat;

public class SpawnCommandShould
{
    private const string Usage = "Usage: /spawn <creature id or name> [count 1-50] [radius 1-40] [idle]";

    [Theory]
    [InlineData(AccountAccessLevel.GameMaster, true)]
    [InlineData(AccountAccessLevel.Admin, true)]
    [InlineData(AccountAccessLevel.Player, false)]
    [InlineData(AccountAccessLevel.Tournament, false)]
    [InlineData(AccountAccessLevel.PTR, false)]
    public void Run_for_game_masters_and_admins_only(AccountAccessLevel level, bool runs)
    {
        var fixture = new Fixture(level);

        Assert.Equal(runs, fixture.Dispatch("/spawn 5"));
        Assert.Equal(runs ? 1 : 0, fixture.Spawns().Count);
    }

    [Fact]
    public void Spawn_creatures_around_the_character_that_leave_after_ten_minutes()
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        fixture.Dispatch("/spawn 5 20 15");

        List<(CreatureTemplateId Template, Vector3 Near, bool WithScript)> spawns = fixture.Spawns();
        Assert.Equal(20, spawns.Count);
        Assert.All(spawns, s =>
        {
            Assert.Equal(new CreatureTemplateId(5), s.Template);
            Assert.True(s.WithScript);
            Assert.InRange(fixture.DistanceOnGround(s.Near), 0f, 15f);
        });
        fixture.Host.Received(20).DespawnAfter(Arg.Any<ICreature>(), TimeSpan.FromMinutes(10));
        Assert.Equal("Spawned 20 Grey Fen Wolf.", fixture.LastMessage());
    }

    [Fact]
    public void Spawn_idle_creatures_without_their_script()
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        fixture.Dispatch("/spawn grey fen wolf 3 IDLE");

        Assert.Equal(3, fixture.Spawns().Count);
        Assert.All(fixture.Spawns(), s => Assert.False(s.WithScript));
        Assert.Equal("Spawned 3 Grey Fen Wolf (idle).", fixture.LastMessage());
    }

    [Theory]
    [InlineData("5", 5ul)]
    [InlineData("boar", 4ul)]
    [InlineData("Grey Fen", 5ul)]
    [InlineData("WOLF", 11ul)]   // named exactly, although "Grey Fen Wolf" contains it
    [InlineData("999", 0ul)]
    [InlineData("dragon", 0ul)]
    [InlineData("r", 0ul)]       // in three names, none of them exactly
    public void Find_the_template_an_id_or_a_name_names(string text, ulong expected)
    {
        CreatureTemplate? found = SpawnCommand.Find(Fixture.Templates, text, out IReadOnlyList<CreatureTemplate> candidates);

        Assert.Equal(expected, found?.Id.Value ?? 0ul);
        Assert.Equal(text == "r" ? 3 : 0, candidates.Count);
    }

    [Fact]
    public void Treat_two_templates_of_one_name_as_ambiguous()
    {
        CreatureTemplate[] templates = [.. Fixture.Templates, Template(12, "Thornback Boar")];

        Assert.Null(SpawnCommand.Find(templates, "thornback boar", out IReadOnlyList<CreatureTemplate> candidates));
        Assert.Equal([4ul, 12ul], candidates.Select(t => t.Id.Value));
    }

    [Theory]
    [InlineData("/spawn", Usage)]
    [InlineData("/spawn 5 0", Usage)]
    [InlineData("/spawn 5 51", Usage)]
    [InlineData("/spawn 5 abc", Usage)]
    [InlineData("/spawn 5 2 0.5", Usage)]
    [InlineData("/spawn 5 2 41", Usage)]
    [InlineData("/spawn 5 2 10 3", Usage)]
    [InlineData("/spawn 5 2 NaN", Usage)]
    [InlineData("/spawn 5 2 Infinity", Usage)]
    [InlineData("/spawn 5 1e3", Usage)]
    [InlineData("/spawn wolf -5", Usage)]
    [InlineData("/spawn 999", "No creature template matches '999'.")]
    [InlineData("/spawn dragon", "No creature template matches 'dragon'.")]
    [InlineData("/spawn r", "'r' matches several: 4 Thornback Boar, 5 Grey Fen Wolf, 8 Bramblemaw Alpha.")]
    public void Refuse_and_spawn_nothing(string message, string reply)
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        fixture.Dispatch(message);

        Assert.Empty(fixture.Spawns());
        Assert.Equal(reply, fixture.LastMessage());
    }

    [Fact]
    public void Read_a_decimal_radius_the_same_in_any_culture()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-PT");
        try
        {
            var fixture = new Fixture(AccountAccessLevel.GameMaster);

            fixture.Dispatch("/spawn 5 2 12.5");

            // Two points of a 12.5 m disc stand at 6.25 m and 10.8 m: past 10 only if 12.5 was read as 12.5.
            var distances = fixture.Spawns().Select(s => fixture.DistanceOnGround(s.Near)).ToList();
            Assert.Equal(2, distances.Count);
            Assert.InRange(distances.Max(), 10f, 12.5f);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData(2, "Spawned 3 Grey Fen Wolf; 2 found no ground.")]
    [InlineData(5, "No Grey Fen Wolf could be placed: no ground nearby.")]
    public void Count_the_points_that_found_no_ground(int withoutGround, string reply)
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster, withoutGround);

        fixture.Dispatch("/spawn 5 5");

        Assert.Equal(5, fixture.Spawns().Count);
        fixture.Host.Received(5 - withoutGround).DespawnAfter(Arg.Any<ICreature>(), Arg.Any<TimeSpan>());
        Assert.Equal(reply, fixture.LastMessage());
    }

    [Fact]
    public void Spread_the_points_over_the_whole_disc()
    {
        var centre = new Vector3(100, 3, 200);

        IReadOnlyList<Vector3> points = SpawnCommand.Points(centre, 50, 10f);

        var distances = points.Select(p => Ground(p - centre)).ToList();
        Assert.Equal(50, points.Count);
        Assert.All(points, p => Assert.Equal(3f, p.y));
        Assert.All(distances, d => Assert.InRange(d, 0f, 10f));
        Assert.Contains(distances, d => d > 9f);
        Assert.Contains(distances, d => d < 2f);
        for (int i = 0; i < points.Count; i++)
        {
            for (int j = i + 1; j < points.Count; j++)
                Assert.True(Ground(points[i] - points[j]) > 0.5f, $"points {i} and {j} are within 0.5 m");
        }
    }

    private static float Ground(Vector3 v) => MathF.Sqrt(v.x * v.x + v.z * v.z);

    private sealed class Fixture
    {
        public static readonly IReadOnlyList<CreatureTemplate> Templates =
        [
            Template(4, "Thornback Boar"), Template(5, "Grey Fen Wolf"), Template(8, "Bramblemaw Alpha"),
            Template(11, "Wolf"),
        ];

        private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
        private readonly ICreaturePlacementService _placement = Substitute.For<ICreaturePlacementService>();
        private readonly List<OutboundPacket> _sent = [];
        private readonly CommandDispatcher _dispatcher;

        public Fixture(AccountAccessLevel level, int withoutGround = 0)
        {
            Character = new CharacterEntity(NullLoggerFactory.Instance,
                new Character { Id = 1u, Health = 100 }, new RegenConfiguration())
            {
                Position = new Vector3(100, 3, 200),
                InstanceId = Guid.NewGuid(),
            };
            _connection.Character.Returns(Character);
            _connection.AccessLevel.Returns(level);
            _connection.When(c => c.Send(Arg.Any<OutboundPacket>()))
                .Do(call => _sent.Add(call.Arg<OutboundPacket>()));

            StaticData data = TestStaticData.LoadAsync(TestStaticData.Repositories(creatures: () => [.. Templates]))
                .GetAwaiter().GetResult();
            IWorld world = Substitute.For<IWorld>();
            world.Data.Returns(data);
            world.InstanceRegistry.GetInstanceById(Character.InstanceId).Returns(Host);

            int calls = 0;
            _placement.SpawnAt(Host, Arg.Any<CreatureTemplateId>(), Arg.Any<Vector3>(), Arg.Any<bool>())
                .Returns(_ => calls++ < withoutGround ? null : Substitute.For<ICreature>());

            _dispatcher = new CommandDispatcher(
                [new SpawnCommand(world, _placement, NullLogger<SpawnCommand>.Instance)],
                NullLogger<CommandDispatcher>.Instance, TimeProvider.System);
        }

        public CharacterEntity Character { get; }

        public IItemUseHost Host { get; } = Substitute.For<IItemUseHost>();

        public bool Dispatch(string message) => _dispatcher.Dispatch(_connection,
            new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow });

        public float DistanceOnGround(Vector3 point) => Ground(point - Character.Position);

        public List<(CreatureTemplateId Template, Vector3 Near, bool WithScript)> Spawns() => _placement.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ICreaturePlacementService.SpawnAt))
            .Select(call => call.GetArguments())
            .Select(args => ((CreatureTemplateId)args[1]!, (Vector3)args[2]!, (bool)args[3]!))
            .ToList();

        public string LastMessage() => _sent
            .Where(packet => packet.Header.Type == NetworkPacketType.SMSG_CHAT_MESSAGE)
            .Select(packet =>
            {
                using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
                return Serializer.Deserialize<SChatMessagePacket>(stream).Message;
            })
            .Last();

    }

    private static CreatureTemplate Template(ulong id, string name) =>
        new() { Id = new CreatureTemplateId(id), Name = name, MinLevel = 1, MaxLevel = 1 };
}

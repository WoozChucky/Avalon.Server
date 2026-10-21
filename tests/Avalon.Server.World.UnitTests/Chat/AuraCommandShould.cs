using System.Globalization;
using Avalon.Common;
using Avalon.Common.Accounts;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.Chat;
using Avalon.World.Entities;
using Avalon.World.Items;
using Avalon.World.Public;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;

namespace Avalon.Server.World.UnitTests.Chat;

public class AuraCommandShould
{
    private const string Usage = "Usage: /aura <aura id> [seconds, up to 600] or /aura clear";

    [Theory]
    [InlineData(AccountAccessLevel.GameMaster, true)]
    [InlineData(AccountAccessLevel.Admin, true)]
    [InlineData(AccountAccessLevel.Player, false)]
    [InlineData(AccountAccessLevel.Tournament, false)]
    [InlineData(AccountAccessLevel.PTR, false)]
    public void Run_for_game_masters_and_admins_only(AccountAccessLevel level, bool runs)
    {
        var fixture = new Fixture(level);

        Assert.Equal(runs, fixture.Dispatch("/aura 920 5"));
        Assert.Equal(runs ? 1 : 0, fixture.Character.Auras.Count);
        Assert.Equal(runs, fixture.Dispatch("/aura clear"));
        Assert.Equal(0, fixture.Character.Auras.Count);
    }

    [Theory]
    [InlineData("/aura")]
    [InlineData("/aura stun")]
    [InlineData("/aura -920")]
    [InlineData("/aura 920 abc")]
    [InlineData("/aura 920 0")]
    [InlineData("/aura 920 -1")]
    [InlineData("/aura 920 601")]
    [InlineData("/aura 920 600.001")]
    [InlineData("/aura 920 0.0001")]
    [InlineData("/aura 920 1e2")]
    [InlineData("/aura 920 NaN")]
    [InlineData("/aura 920 5 6")]
    [InlineData("/aura clear 5")]
    public void Answer_the_usage_and_apply_nothing(string message)
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        fixture.Dispatch(message);

        Assert.Empty(fixture.Character.Auras.All);
        Assert.Equal(Usage, fixture.LastMessage());
    }

    [Fact]
    public void Name_an_id_no_aura_has()
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        fixture.Dispatch("/aura 999 5");

        Assert.Empty(fixture.Character.Auras.All);
        Assert.Equal("No aura has id 999.", fixture.LastMessage());
    }

    /// <summary>
    /// The seconds given replace the row's 1.5 s exactly, from nobody, so no combat is entered; a second /aura replaces
    /// the first's time even where it is shorter; without seconds the row's own duration applies.
    /// </summary>
    [Fact]
    public void Apply_the_aura_to_yourself_for_the_seconds_given()
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);

        fixture.Dispatch("/aura 920 30");

        ActiveAura stun = Assert.Single(fixture.Character.Auras.All);
        Assert.Equal((30_000u, 30_000u), (stun.DurationMs, UnitAuras.RemainingMs(stun, fixture.Harness.Time.GetUtcNow())));
        Assert.Equal(new ObjectGuid(), stun.CasterGuid);
        Assert.Null(fixture.Harness.Combat.GetEncounterFor(fixture.Character));
        Assert.Equal("Applied Stunned (920) for 30 s.", fixture.LastMessage());

        fixture.Dispatch("/aura 920 2.5");
        Assert.Equal(2500u, Assert.Single(fixture.Character.Auras.All).DurationMs);
        Assert.Equal("Refreshed Stunned (920) for 2.5 s.", fixture.LastMessage());

        fixture.Dispatch("/aura 901");
        Assert.Equal(12_000u, fixture.Character.Auras.Find(new AuraId(901), null)!.DurationMs);
        Assert.Equal("Applied Bleed (901) for 12 s.", fixture.LastMessage());
    }

    [Fact]
    public void Read_decimal_seconds_the_same_in_any_culture()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-PT");
        try
        {
            var fixture = new Fixture(AccountAccessLevel.GameMaster);

            fixture.Dispatch("/aura 920 600.0");
            Assert.Equal(600_000u, Assert.Single(fixture.Character.Auras.All).DurationMs);

            fixture.Dispatch("/aura 920 1.25");
            Assert.Equal(1250u, Assert.Single(fixture.Character.Auras.All).DurationMs);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Clear_the_harmful_auras_and_keep_the_helpful_ones()
    {
        var fixture = new Fixture(AccountAccessLevel.GameMaster);
        fixture.Dispatch("/aura 920 30");
        fixture.Dispatch("/aura 901");
        fixture.Dispatch("/aura 904");

        fixture.Dispatch("/aura CLEAR");

        Assert.Equal(new AuraId(904), Assert.Single(fixture.Character.Auras.All).Id);
        Assert.Equal("Removed 2 harmful auras.", fixture.LastMessage());

        fixture.Dispatch("/aura clear");
        Assert.Equal("You hold no harmful auras.", fixture.LastMessage());
    }

    [Fact]
    public void Say_why_an_aura_was_refused()
    {
        var dead = new Fixture(AccountAccessLevel.GameMaster);
        dead.Character.IsDead = true;
        dead.Dispatch("/aura 920 5");
        Assert.Empty(dead.Character.Auras.All);
        Assert.Equal("Stunned (920) was refused: you are dead.", dead.LastMessage());

        var arriving = new Fixture(AccountAccessLevel.GameMaster);
        Assert.Equal(AuraApplyResult.Applied, ArrivalProtection.Start(arriving.Character, arriving.Harness.Auras));
        arriving.Dispatch("/aura 920 5");
        Assert.Equal(ArrivalProtectionAura.Id, Assert.Single(arriving.Character.Auras.All).Id.Value);
        Assert.Equal("Stunned (920) was refused: arrival protection passes harmful auras over until you move or act.",
            arriving.LastMessage());

        var full = new Fixture(AccountAccessLevel.GameMaster, maxAurasPerUnit: 1);
        full.Dispatch("/aura 904");
        full.Dispatch("/aura 920 5");
        Assert.Equal(new AuraId(904), Assert.Single(full.Character.Auras.All).Id);
        Assert.Equal("Stunned (920) was refused: you hold the most auras Game:MaxAurasPerUnit allows.", full.LastMessage());
    }

    private sealed class Fixture
    {
        private readonly IWorldConnection _connection = Substitute.For<IWorldConnection>();
        private readonly List<OutboundPacket> _sent = [];
        private readonly CommandDispatcher _dispatcher;

        public Fixture(AccountAccessLevel level, int maxAurasPerUnit = 32)
        {
            Harness = new AuraHarness(maxAurasPerUnit);
            Harness.Use(AuraTestData.Stunned(), AuraTestData.Bleed(), AuraTestData.Renew(),
                AuraTestData.ArrivalProtection());
            Character = TestCharacters.New(1, time: Harness.Time);
            Character.Health = Character.CurrentHealth = 500;
            Character.InstanceId = Guid.NewGuid();
            Harness.Enter(Character);

            _connection.Character.Returns(Character);
            _connection.AccessLevel.Returns(level);
            _connection.When(c => c.Send(Arg.Any<OutboundPacket>()))
                .Do(call => _sent.Add(call.Arg<OutboundPacket>()));

            IItemUseHost host = Substitute.For<IItemUseHost>();
            host.Auras.Returns(Harness.Auras);
            IWorld world = Substitute.For<IWorld>();
            world.InstanceRegistry.GetInstanceById(Character.InstanceId).Returns(host);

            _dispatcher = new CommandDispatcher([new AuraCommand(world, NullLogger<AuraCommand>.Instance)],
                NullLogger<CommandDispatcher>.Instance, TimeProvider.System);
        }

        public AuraHarness Harness { get; }

        public CharacterEntity Character { get; }

        public bool Dispatch(string message) => _dispatcher.Dispatch(_connection,
            new CChatMessagePacket { Message = message, DateTime = DateTime.UtcNow });

        public string LastMessage() => _sent
            .Where(packet => packet.Header.Type == NetworkPacketType.SMSG_CHAT_MESSAGE)
            .Select(packet =>
            {
                using var stream = new MemoryStream(packet.PayloadMemory.ToArray());
                return Serializer.Deserialize<SChatMessagePacket>(stream).Message;
            })
            .Last();
    }
}

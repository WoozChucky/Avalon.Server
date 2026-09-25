using System.IO;
using Avalon.Common.Accounts;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Social;
using Avalon.World;
using Avalon.World.Chat;
using Avalon.World.Public;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;
using Xunit;

namespace Avalon.Server.World.UnitTests.Chat;

public class ReloadCommandShould
{
    [Fact]
    public async Task Reply_With_The_Summary_When_Dialogue_Reloads_Successfully()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Dialogue, true, "14 texts, 6 nodes, 9 options", TimeSpan.FromMilliseconds(38), null));

        await fixture.Execute("dialogue");

        Assert.Equal(
            ["Reloaded dialogue: 14 texts, 6 nodes, 9 options (38 ms)."],
            fixture.CaptureSentMessages());
        await fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Dialogue })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_With_A_Spawn_Caveat_When_Creatures_Reload_Successfully()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Creatures, true, "10 templates, 10 base stats, 4 rarities", TimeSpan.FromMilliseconds(21), null));

        await fixture.Execute("creatures");

        Assert.Equal(
            ["Reloaded creatures: 10 templates, 10 base stats, 4 rarities (21 ms). Affects new spawns only."],
            fixture.CaptureSentMessages());
        await fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Creatures })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_With_The_Exception_Type_When_A_Reload_Fails()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Creatures, false, string.Empty, TimeSpan.FromMilliseconds(5),
            new InvalidOperationException("boom")));

        await fixture.Execute("creatures");

        Assert.Equal(
            ["Reload of creatures failed: InvalidOperationException. Nothing changed."],
            fixture.CaptureSentMessages());
    }

    [Fact]
    public async Task Reply_Sensibly_When_A_Failure_Has_No_Exception()
    {
        // Defends against outcome.Error?.GetType().Name throwing, or producing garbage, when a
        // failed outcome carries no exception at all.
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Creatures, false, string.Empty, TimeSpan.Zero, null));

        await fixture.Execute("creatures");

        string message = Assert.Single(fixture.CaptureSentMessages());
        Assert.Equal("Reload of creatures failed: . Nothing changed.", message);
    }

    [Fact]
    public async Task Reply_With_One_Line_Per_Area_In_Order_For_All()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(
            new ReloadOutcome(ReloadArea.Dialogue, true, "1 texts, 1 nodes, 1 options", TimeSpan.FromMilliseconds(1), null),
            new ReloadOutcome(ReloadArea.Creatures, false, string.Empty, TimeSpan.FromMilliseconds(2),
                new InvalidOperationException()),
            new ReloadOutcome(ReloadArea.Abilities, true, "1 ability templates", TimeSpan.FromMilliseconds(3), null),
            new ReloadOutcome(ReloadArea.Items, true, "1 item templates", TimeSpan.FromMilliseconds(4), null),
            new ReloadOutcome(ReloadArea.Progression, true, "1 levels, 1 class stats, 1 create infos",
                TimeSpan.FromMilliseconds(5), null));

        await fixture.Execute("all");

        Assert.Equal(
            [
                "Reloaded dialogue: 1 texts, 1 nodes, 1 options (1 ms).",
                "Reload of creatures failed: InvalidOperationException. Nothing changed.",
                "Reloaded abilities: 1 ability templates (3 ms).",
                "Reloaded items: 1 item templates (4 ms).",
                "Reloaded progression: 1 levels, 1 class stats, 1 create infos (5 ms)."
            ],
            fixture.CaptureSentMessages());
        await fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(Enum.GetValues<ReloadArea>())),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("maps")]
    [InlineData("chunks")]
    public async Task Refuse_To_Reload_Maps_Or_Chunks(string area)
    {
        Fixture fixture = Fixture.Build();

        await fixture.Execute(area);

        Assert.Equal(
            ["Maps and chunk layouts cannot be reloaded: live instances have already baked a navmesh " +
             "from them. Restart the world server."],
            fixture.CaptureSentMessages());
        await fixture.Reloader.DidNotReceiveWithAnyArgs().ReloadAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("99")]
    public async Task Reply_With_Usage_For_An_Unrecognized_Area(string area)
    {
        Fixture fixture = Fixture.Build();

        await fixture.Execute(area);

        Assert.Equal(
            ["Usage: /reload <dialogue|creatures|abilities|items|progression|all>"],
            fixture.CaptureSentMessages());
        await fixture.Reloader.DidNotReceiveWithAnyArgs().ReloadAsync(default!, default);
    }

    [Fact]
    public async Task Accept_Area_Names_Case_Insensitively()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Dialogue, true, "1 texts, 1 nodes, 1 options", TimeSpan.FromMilliseconds(1), null));

        await fixture.Execute("DIALOGUE");

        await fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Dialogue })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Require_Game_Master_Access()
    {
        Fixture fixture = Fixture.Build();

        Assert.Equal(AccessLevels.GameMaster, fixture.Command.RequiredAccess);
    }

    private sealed class Fixture
    {
        public IReferenceDataReloader Reloader { get; private init; } = null!;
        public ReloadCommand Command { get; private init; } = null!;
        private IWorldConnection Connection { get; init; } = null!;
        private List<NetworkPacket> SentPackets { get; init; } = null!;

        public static Fixture Build()
        {
            IReferenceDataReloader reloader = Substitute.For<IReferenceDataReloader>();

            IWorldConnection connection = Substitute.For<IWorldConnection>();
            connection.CryptoSession.Returns(new FakeAvalonCryptoSession());

            var sentPackets = new List<NetworkPacket>();
            connection.When(c => c.Send(Arg.Any<NetworkPacket>()))
                .Do(ci => sentPackets.Add(ci.Arg<NetworkPacket>()));

            return new Fixture
            {
                Reloader = reloader,
                Connection = connection,
                SentPackets = sentPackets,
                Command = new ReloadCommand(reloader, NullLogger<ReloadCommand>.Instance)
            };
        }

        /// <summary>Stubs ReloadAsync to return the given outcomes, in order, as one report.</summary>
        public void Returns(params ReloadOutcome[] outcomes)
        {
            Reloader.ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ReloadReport(outcomes)));
        }

        public Task Execute(string area)
        {
            string[] args = string.IsNullOrEmpty(area) ? [] : [area];
            var ctx = new WorldPacketContext<CChatMessagePacket>
            {
                Packet = new CChatMessagePacket { Message = $"/reload {area}", DateTime = DateTime.UtcNow },
                Connection = Connection
            };
            return Command.ExecuteAsync(ctx, args).WaitAsync(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Payload bytes are unencrypted: FakeAvalonCryptoSession.Encrypt is a pass-through, so what
        /// SChatMessagePacket.Create wrote is exactly what protobuf-net reads back here. Mirrors
        /// InteractHandlerShould.CaptureSentNode.
        /// </summary>
        public List<string> CaptureSentMessages()
        {
            return SentPackets
                .Where(p => p.Header.Type == NetworkPacketType.SMSG_CHAT_MESSAGE)
                .Select(p =>
                {
                    using var stream = new MemoryStream(p.Payload);
                    return Serializer.Deserialize<SChatMessagePacket>(stream).Message;
                })
                .ToList();
        }
    }
}

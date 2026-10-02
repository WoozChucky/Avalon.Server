using Avalon.Common.Accounts;
using Avalon.Network.Packets.Social;
using Avalon.World.Chat;
using Avalon.World.Reload;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Chat;

public class ReloadCommandShould
{
    [Fact]
    public void Reply_With_The_Summary_When_Dialogue_Reloads_Successfully()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Dialogue, true, "14 texts, 6 nodes, 9 options", TimeSpan.FromMilliseconds(38), null));

        fixture.Execute("dialogue");

        Assert.Equal(
            ["Reloaded dialogue: 14 texts, 6 nodes, 9 options (38 ms)."],
            fixture.CaptureSentMessages());
        _ = fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Dialogue })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Reply_With_A_Next_Kill_Caveat_When_Loot_Reloads_Successfully()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Loot, true, "8 tables, 40 entries", TimeSpan.FromMilliseconds(12), null));

        fixture.Execute("loot");

        Assert.Equal(
            ["Reloaded loot: 8 tables, 40 entries (12 ms). Affects the next kill; drops already on the ground keep what they rolled."],
            fixture.CaptureSentMessages());
    }

    [Fact]
    public void Reply_With_A_Stock_Caveat_When_Vendors_Reload_Successfully()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Vendors, true, "3 vendors, 31 rows", TimeSpan.FromMilliseconds(9), null));

        fixture.Execute("vendors");

        Assert.Equal(
            ["Reloaded vendors: 3 vendors, 31 rows (9 ms). Open shops get the new list on the next tick; live stock counts carry over by row."],
            fixture.CaptureSentMessages());
    }

    [Fact]
    public void Reach_the_combat_area_and_say_it_is_forward_only()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Combat, true, "1 formula, 4 class stat factors", TimeSpan.FromMilliseconds(3), null));

        fixture.Execute("combat");

        Assert.Equal(
            ["Reloaded combat: 1 formula, 4 class stat factors (3 ms). Affects the next hit; a character's stats change at its next select, gear change or level-up."],
            fixture.CaptureSentMessages());
        _ = fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Combat })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Reply_With_A_Spawn_Caveat_When_Creatures_Reload_Successfully()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Creatures, true, "10 templates, 10 base stats, 4 rarities", TimeSpan.FromMilliseconds(21), null));

        fixture.Execute("creatures");

        Assert.Equal(
            ["Reloaded creatures: 10 templates, 10 base stats, 4 rarities (21 ms). Affects new spawns only."],
            fixture.CaptureSentMessages());
        _ = fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Creatures })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Reply_With_The_Exception_Type_When_A_Reload_Fails()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Creatures, false, string.Empty, TimeSpan.FromMilliseconds(5),
            new InvalidOperationException("boom")));

        fixture.Execute("creatures");

        Assert.Equal(
            ["Reload of creatures failed: InvalidOperationException. Nothing changed."],
            fixture.CaptureSentMessages());
    }

    [Fact]
    public void Reply_Sensibly_When_A_Failure_Has_No_Exception()
    {
        // Defends against outcome.Error?.GetType().Name throwing, or producing garbage, when a
        // failed outcome carries no exception at all.
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Creatures, false, string.Empty, TimeSpan.Zero, null));

        fixture.Execute("creatures");

        string message = Assert.Single(fixture.CaptureSentMessages());
        Assert.Equal("Reload of creatures failed: . Nothing changed.", message);
    }

    [Fact]
    public void Reply_With_One_Line_Per_Area_In_Order_For_All()
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

        fixture.Execute("all");

        Assert.Equal(
            [
                "Reloaded dialogue: 1 texts, 1 nodes, 1 options (1 ms).",
                "Reload of creatures failed: InvalidOperationException. Nothing changed.",
                "Reloaded abilities: 1 ability templates (3 ms).",
                "Reloaded items: 1 item templates (4 ms).",
                "Reloaded progression: 1 levels, 1 class stats, 1 create infos (5 ms)."
            ],
            fixture.CaptureSentMessages());
        _ = fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(Enum.GetValues<ReloadArea>())),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("maps")]
    [InlineData("chunks")]
    public void Refuse_To_Reload_Maps_Or_Chunks(string area)
    {
        Fixture fixture = Fixture.Build();

        fixture.Execute(area);

        Assert.Equal(
            ["Maps and chunk layouts cannot be reloaded: live instances have already baked a navmesh " +
             "from them. Restart the world server."],
            fixture.CaptureSentMessages());
        _ = fixture.Reloader.DidNotReceiveWithAnyArgs().ReloadAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("99")]
    public void Reply_With_Usage_For_An_Unrecognized_Area(string area)
    {
        Fixture fixture = Fixture.Build();

        fixture.Execute(area);

        Assert.Equal(
            ["Usage: /reload <dialogue|creatures|abilities|items|progression|loot|vendors|combat|quests|auras|all>"],
            fixture.CaptureSentMessages());
        _ = fixture.Reloader.DidNotReceiveWithAnyArgs().ReloadAsync(default!, default);
    }

    [Fact]
    public void Accept_Area_Names_Case_Insensitively()
    {
        Fixture fixture = Fixture.Build();
        fixture.Returns(new ReloadOutcome(
            ReloadArea.Dialogue, true, "1 texts, 1 nodes, 1 options", TimeSpan.FromMilliseconds(1), null));

        fixture.Execute("DIALOGUE");

        _ = fixture.Reloader.Received(1).ReloadAsync(
            Arg.Is<IReadOnlyList<ReloadArea>>(a => a.SequenceEqual(new[] { ReloadArea.Dialogue })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Require_Game_Master_Access()
    {
        Fixture fixture = Fixture.Build();

        Assert.Equal(AccessLevels.GameMaster, fixture.Command.RequiredAccess);
    }

    [Fact]
    public void Hand_a_failed_reload_task_to_the_failure_path()
    {
        Fixture fixture = Fixture.Build();
        fixture.Reloader.ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ReloadReport>(new InvalidOperationException("db down")));

        fixture.Execute("dialogue");

        Assert.IsType<InvalidOperationException>(Assert.Single(fixture.Failures));
        Assert.Empty(fixture.CaptureSentMessages());
    }

    private sealed class Fixture
    {
        public IReferenceDataReloader Reloader { get; private init; } = null!;
        public ReloadCommand Command { get; private init; } = null!;
        private CommandConnection Connection { get; init; } = null!;
        public List<Exception> Failures { get; } = [];

        public static Fixture Build()
        {
            IReferenceDataReloader reloader = Substitute.For<IReferenceDataReloader>();

            return new Fixture
            {
                Reloader = reloader,
                Connection = new CommandConnection(AccountAccessLevel.GameMaster),
                Command = new ReloadCommand(reloader, NullLogger<ReloadCommand>.Instance)
            };
        }

        /// <summary>Stubs ReloadAsync to return the given outcomes, in order, as one report.</summary>
        public void Returns(params ReloadOutcome[] outcomes)
        {
            Reloader.ReloadAsync(Arg.Any<IReadOnlyList<ReloadArea>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ReloadReport(outcomes)));
        }

        public void Execute(string area)
        {
            string[] args = string.IsNullOrEmpty(area) ? [] : [area];
            var ctx = new CommandContext(Connection.Connection,
                new CChatMessagePacket { Message = $"/reload {area}", DateTime = DateTime.UtcNow }, Failures.Add);
            Command.Execute(ctx, args);
        }

        /// <summary>Every chat line sent; the crypto session is a pass-through (see CommandConnection).</summary>
        public List<string> CaptureSentMessages() => Connection.Messages();
    }
}

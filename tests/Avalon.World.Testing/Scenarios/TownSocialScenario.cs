using System.Runtime.CompilerServices;
using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.World.Chat;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Maps.Navigation;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.World.Testing.Scenarios;

/// <summary>
/// A sociable town: one town at its cap, 30 players (a town's <c>MaxPlayers</c>), in ten parties of three, walking the
/// town's 8 m loop through the real input handler as in <c>town-walk</c> (with no creatures), while their parties keep
/// changing and they chat, through the real party and chat handlers (<see cref="SocialSchedule" />): in every second a
/// leader leaves, is invited back and accepts, one player says a line to the town, one whispers, one speaks to its
/// party and one runs <c>/ignorelist</c>. Each player leaves, is invited and accepts once every 30 s, chats every 10 s
/// and runs the command every 30 s. What is measured is a busy town's walking and broadcast plus the party service, the
/// chat handler, the command dispatcher, the rate limit and the ignore check, and the rosters, results and lines they
/// send.
/// </summary>
/// <remarks>
/// Measured as the other steady scenarios are, by the least of five windows: the schedule repeats every window, so every
/// window holds one of each event, made by players in the same state (see <see cref="SocialSchedule" />). The world has
/// the World server's reference data, for the party service and level 1 characters of a class (the classes cycle over
/// all four), and the rate limits as production sets them (<c>Game:ChatMessagesPerMinute</c> 10 and, #944,
/// <c>Game:PartyInvitesPerMinute</c> 30, which an inviter's one invite every 30 s stays inside). The characters are named
/// "Socialaa" to "Socialbd": the invite and the whisper find a player by a name of letters only.
/// </remarks>
public sealed class TownSocialScenario : IScenario
{
    /// <summary>The rate limit production's <c>appsettings.json</c> sets: 10 messages a minute per character.</summary>
    public const int ChatMessagesPerMinute = 10;

    /// <summary>The invite budget production's <c>appsettings.json</c> sets: 30 invites a minute per character (#944).</summary>
    public const int PartyInvitesPerMinute = 30;

    private static readonly CharacterClass[] s_classes =
        [CharacterClass.Warrior, CharacterClass.Wizard, CharacterClass.Hunter, CharacterClass.Healer];

    // Each world's schedule, for Verify: the scenario is shared, and builds a world for every run.
    private readonly ConditionalWeakTable<ScenarioWorld, SocialSchedule> _schedules = new();

    public string Name => "town-social";

    /// <summary>A town's cap (<c>MaxPlayers</c>, 30 for the town and the default where a template sets none).</summary>
    public int Players => 30;

    /// <summary>
    /// One minute of the world's time, the chat rate limit's window (<see cref="ChatRateLimiter.Window" />), rather than
    /// the default ten seconds (#1030). The limit keeps a queue of send times per character, made with its first line
    /// and grown once, from four places to eight, with its fifth line inside the window. Every player speaks every 10 s,
    /// three of them each second, so the queues are made in seconds 0 to 9 and grown in seconds 40 to 49 (88 B each,
    /// 264 B a second); none holds more than six lines after that, and from second 50 on every second reads the same.
    /// A minute puts the windows ten seconds past the last growth, after two whole turns of the parties (30 s each).
    /// The invite budget (#944) keeps a queue the same way, per inviter: each invites once every 30 s, so every queue is
    /// made in the first turn and never holds more than two, which its first four places take.
    /// </summary>
    public int SteadyWarmupTicks => (int)ChatRateLimiter.Window.TotalSeconds * ScenarioMeasurement.WindowTicks;

    public ScenarioWorld Build()
    {
        var world = ScenarioWorld.CreateWithReferenceData(
            new GameConfiguration
            {
                ChatMessagesPerMinute = ChatMessagesPerMinute,
                PartyInvitesPerMinute = PartyInvitesPerMinute,
            });
        try
        {
            MapInstance town = world.AddInstance(MapType.Town);

            var navigator = new MapNavigator(NullLoggerFactory.Instance);
            navigator.LoadFromNavMesh(TownNavmesh.Shared);
            if (navigator.FindGround(TownNavmesh.LoopCentre, out Vector3 centre) != NavmeshGroundKind.Under)
                throw new InvalidOperationException("The loop's centre is off the town's navmesh");

            PlayerInputHandler input = LoopWalker.Handler(world);
            var social = new SocialSchedule(world, Players);
            var connections = new ScenarioConnection[Players];
            for (int p = 0; p < Players; p++)
            {
                CharacterEntity character = world.NewCharacter(s_classes[p % s_classes.Length]);
                character.Name = SocialSchedule.NameOf(p);

                Action<ScenarioConnection> walk = LoopWalker.Around(input, centre);
                int player = p;
                connections[p] = world.Connect(character, connection =>
                {
                    walk(connection);
                    social.Step(player, connection);
                });
                social.Add(p, connections[p]);
            }

            for (int p = 0; p < Players; p += SocialSchedule.PartySize)
                world.FormParty(connections[p..(p + SocialSchedule.PartySize)]);

            for (int p = 0; p < Players; p++)
                world.Join(town, connections[p], LoopWalker.LoopPoint(navigator, centre, p * MathF.Tau / Players));

            world.Marked += social.Mark;
            _schedules.AddOrUpdate(world, social);
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Every player present, sent to and walking, and, over the measured windows, every event of the schedule done and
    /// checked, each at least as many times as there are windows (the schedule puts each once in every window):
    /// leaders leaving, invites sent and accepted, lines said to the town, whispers reaching their target, party lines
    /// reaching every member, and the command run. A town that stopped any of them would measure cheaper, and the
    /// scenario would pass the allocation gate as an improvement.
    /// </summary>
    public void Verify(ScenarioWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.RequireEveryPresent(Name);
        world.RequireEverySent(Name);
        world.RequireEveryWalked(Name, LoopWalker.MeasuredInputs);

        SocialSchedule social = _schedules.TryGetValue(world, out SocialSchedule? found)
            ? found
            : throw new ArgumentException($"The world was not built by {Name}", nameof(world));

        if (social.FirstFailure is { } failure)
            throw new InvalidOperationException($"{Name}: {failure}. {social.Counts.Failures} checks failed in all.");

        SocialCounts measured = social.SinceMark;
        RequireEachWindow(measured.Left, "leaders that left their party");
        RequireEachWindow(measured.Invited, "invites sent");
        RequireEachWindow(measured.Accepted, "invites accepted");
        RequireEachWindow(measured.Said, "lines said to the town");
        RequireEachWindow(measured.Whispered, "whispers delivered");
        RequireEachWindow(measured.PartyLines, "party lines delivered");
        RequireEachWindow(measured.Commands, "commands run");
    }

    private void RequireEachWindow(int count, string what)
    {
        if (count < ScenarioMeasurement.Windows)
        {
            throw new InvalidOperationException(
                $"{Name}: {count} {what} during the {ScenarioMeasurement.Windows} measured windows, expected one in " +
                "each at least. A town that stopped doing it measures cheaper than it is and would pass the allocation " +
                "gate as an improvement.");
        }
    }
}

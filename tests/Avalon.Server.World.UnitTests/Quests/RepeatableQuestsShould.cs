using System.Globalization;
using Avalon.Domain.World;
using Avalon.Network.Packets.Quest;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// Repeatable quests (#710): a plain repeatable comes back at once, a daily, weekly or monthly one at its next fixed
/// reset; prerequisites and vendor gates only ever ask whether it was done once, doodad gates follow availability.
/// </summary>
public class RepeatableQuestsShould
{
    private const uint Again = 7801, Daily = 7802, Weekly = 7803, Monthly = 7804, After = 7805;

    private static List<QuestTemplate> Quests() =>
    [
        Quest(Again).WithStage(0, Kill(Again * 10 + 1, Boar, 1)).Repeating(null),
        Quest(Daily).WithStage(0, Kill(Daily * 10 + 1, Boar, 1)).Repeating(QuestRepeatFrequency.Daily),
        Quest(Weekly).WithStage(0, Kill(Weekly * 10 + 1, Boar, 1)).Repeating(QuestRepeatFrequency.Weekly),
        Quest(Monthly).WithStage(0, Kill(Monthly * 10 + 1, Boar, 1)).Repeating(QuestRepeatFrequency.Monthly),
        Quest(After, requires: Daily).WithStage(0, Kill(After * 10 + 1, Boar, 1)),
    ];

    /// <summary>#710: each repeat kind comes back by its own rule; prerequisites and vendor gates mean "done once".</summary>
    [Fact]
    public async Task Offer_each_repeat_kind_again_by_its_own_rule()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests());
        QuestClient c = w.Join();
        At(w, "2026-01-02T12:00:00Z");                                       // Friday
        foreach (uint id in (uint[])[Again, Daily, Weekly, Monthly])
            Finish(w, c, id);

        Assert.Equal([QuestResult.Ok, QuestResult.NotAvailable, QuestResult.NotAvailable, QuestResult.NotAvailable, QuestResult.Ok],
            Availability(w, c, Again, Daily, Weekly, Monthly, After));
        Assert.True(Gate(w, c, Again, DoodadGateCondition.NotCompleted));     // a plain repeatable reads "not completed" at once
        Assert.True(Gate(w, c, Daily, DoodadGateCondition.Completed));

        At(w, "2026-01-03T04:00:00Z");                                       // daily reset
        Assert.Equal([QuestResult.Ok, QuestResult.NotAvailable, QuestResult.NotAvailable, QuestResult.Ok],
            Availability(w, c, Daily, Weekly, Monthly, After));
        Assert.True(Gate(w, c, Daily, DoodadGateCondition.NotCompleted));
        Assert.True(QuestProgress.Instance.IsMet(c.Character, Daily, QuestRequirementState.Completed));   // vendor gate: done once

        At(w, "2026-01-03T05:00:00Z");
        w.Accept(c, Daily);                                                  // held across the next reset
        At(w, "2026-01-04T05:00:00Z");
        Finish(w, c, Daily, accept: false);
        Assert.Equal(new QuestCompletion(Daily, Utc("2026-01-02T12:00:00Z"), Utc("2026-01-04T05:00:00Z"), 2),
            c.Character.Quests.Completion(Daily));
        Assert.Equal(QuestResult.NotAvailable, w.Quests.Availability(c.Character, Q(w, Daily)));

        At(w, "2026-01-05T04:00:00Z");                                       // Monday: weekly reset
        Assert.Equal([QuestResult.Ok, QuestResult.Ok, QuestResult.NotAvailable], Availability(w, c, Daily, Weekly, Monthly));
        At(w, "2026-02-01T04:00:00Z");                                       // monthly reset
        Assert.Equal(QuestResult.Ok, w.Quests.Availability(c.Character, Q(w, Monthly)));
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static QuestView Q(QuestTestWorld w, uint id) =>
        w.Data.Quests.TryGet(id, out QuestView? quest) ? quest : throw new InvalidOperationException($"quest {id} did not load");

    /// <summary>Moves the world's clock to <paramref name="iso" /> and advances the reset clock, as the tick does.</summary>
    private static void At(QuestTestWorld w, string iso)
    {
        w.Clock.Advance(Utc(iso) - w.Clock.GetUtcNow().UtcDateTime);
        w.Quests.Resets.Advance();
    }

    /// <summary>Accepts the quest (unless told not to), kills its one boar and turns it in at the giver.</summary>
    private static void Finish(QuestTestWorld w, QuestClient c, uint id, bool accept = true)
    {
        if (accept)
            w.Accept(c, id);
        w.Quests.AddProgress(c.Character, id, id * 10 + 1, 1);
        Creature giver = w.Place(Giver);
        w.Talk(c, giver);
        Assert.Equal(QuestResult.Ok, w.Quests.TurnIn(c.Connection, c.Character, id, giver.Guid.RawValue));
        w.Remove(giver);
    }

    private static bool Gate(QuestTestWorld w, QuestClient c, uint id, DoodadGateCondition condition) =>
        DoodadGates.Allows(new DoodadGate(id, condition, null), c.Character.Quests, w.Data.Quests, w.Quests.Resets);

    private static QuestResult[] Availability(QuestTestWorld w, QuestClient c, params uint[] ids) =>
        ids.Select(id => w.Quests.Availability(c.Character, Q(w, id))).ToArray();
}

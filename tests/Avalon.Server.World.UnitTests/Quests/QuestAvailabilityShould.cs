using Avalon.Network.Packets.Quest;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Entities;
using Avalon.World.Public.Enums;
using Avalon.World.Quests;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>The one availability rule (#433), pure: not done, not held, prerequisite done, level, class, script, room.</summary>
public class QuestAvailabilityShould
{
    private static readonly QuestCatalog Catalog = new(Chain().Append(
            Quest(7299, level: 1).WithStage(0, Kill(72991, Boar, 1))).ToList().Also(q => q[^1].ClassRequirement = CharacterClass.Wizard),
        QuestTestData.Creatures(), Items(), FindScript, NullLoggerFactory.Instance);

    private static QuestView Q(uint id) => Catalog.TryGet(id, out QuestView? q) ? q : throw new InvalidOperationException();

    private static CharacterEntity Character(ushort level = 1)
    {
        CharacterEntity c = TestCharacters.New(1);
        c.Level = level;
        return c;
    }

    private static QuestResult Check(QuestView quest, CharacterEntity c, int max = 20, bool script = true) =>
        QuestAvailability.Check(quest, c, max, () => script);

    [Fact]
    public void Offer_a_quest_whose_rules_are_met() => Assert.Equal(QuestResult.Ok, Check(Q(Hunt), Character()));

    [Fact]
    public void Not_offer_a_quest_already_held_or_already_done()
    {
        CharacterEntity c = Character();
        c.Quests.Start(Hunt, DateTime.UnixEpoch);
        Assert.Equal(QuestResult.NotAvailable, Check(Q(Hunt), c));

        c.Quests.Complete(Hunt, DateTime.UnixEpoch);
        Assert.Equal(QuestResult.NotAvailable, Check(Q(Hunt), c));
    }

    [Fact]
    public void Wait_for_the_prerequisite_to_be_completed()
    {
        CharacterEntity c = Character();
        Assert.Equal(QuestResult.NotAvailable, Check(Q(Tusks), c));

        c.Quests.Start(Hunt, DateTime.UnixEpoch);
        Assert.Equal(QuestResult.NotAvailable, Check(Q(Tusks), c));   // held is not done

        c.Quests.Complete(Hunt, DateTime.UnixEpoch);
        Assert.Equal(QuestResult.Ok, Check(Q(Tusks), c));
    }

    [Fact]
    public void Wait_for_the_level()
    {
        CharacterEntity c = Character(level: 1);
        c.Quests.Complete(Hunt, DateTime.UnixEpoch);
        c.Quests.Complete(Tusks, DateTime.UnixEpoch);

        Assert.Equal(QuestResult.NotAvailable, Check(Q(Howl), c));
        c.Level = 2;
        Assert.Equal(QuestResult.Ok, Check(Q(Howl), c));
    }

    [Fact]
    public void Offer_a_class_quest_to_that_class_only()
    {
        CharacterEntity c = Character();
        Assert.Equal(QuestResult.NotAvailable, Check(Q(7299), c));   // TestCharacters.New is a Warrior

        c.Data!.Class = CharacterClass.Wizard;
        Assert.Equal(QuestResult.Ok, Check(Q(7299), c));
    }

    [Fact]
    public void Let_a_script_narrow_but_never_widen()
    {
        Assert.Equal(QuestResult.NotAvailable, Check(Q(Hunt), Character(), script: false));
        Assert.Equal(QuestResult.NotAvailable, Check(Q(Tusks), Character(), script: true));
    }

    [Fact]
    public void Answer_LogFull_only_for_a_quest_that_is_otherwise_available()
    {
        CharacterEntity c = Character();
        c.Quests.Start(9999, DateTime.UnixEpoch);

        Assert.Equal(QuestResult.LogFull, Check(Q(Hunt), c, max: 1));
        Assert.Equal(QuestResult.NotAvailable, Check(Q(Tusks), c, max: 1));
    }
}

internal static class ListExtensions
{
    public static List<T> Also<T>(this List<T> list, Action<List<T>> change)
    {
        change(list);
        return list;
    }
}

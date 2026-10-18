using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.Characters;
using Avalon.Domain.World;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Quest;
using Avalon.Server.World.UnitTests.Instances;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.Server.World.UnitTests.Parties;
using Avalon.World.Doodads;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Quests;
using Avalon.World.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Avalon.Server.World.UnitTests.Quests.QuestTestData;

namespace Avalon.Server.World.UnitTests.Quests;

/// <summary>
/// A doodad use credits the use-doodad objectives of its user alone (ruling R12), through a real instance and the use
/// service: +1, capped, on each UseDoodad objective for the template in the current stage of an Active quest, settled as a
/// kill is. A party member standing beside the user on the same step gets nothing, and a use that credits nothing
/// allocates nothing. Quest 7801 and doodad guid 7801 are this class's.
/// </summary>
public class QuestDoodadCreditShould
{
    private const uint Errand = 7801, ErrandUse = 78011, ErrandTalk = 78012;

    // Pull the lever, then talk to Marta; and a kill quest that no doodad moves.
    private static List<QuestTemplate> Quests() =>
    [
        Quest(Hunt).WithStage(0, Kill(HuntKill, Boar, 2)),
        Quest(Errand).WithStage(0, UseDoodad(ErrandUse, Lever, 1)).WithStage(1, Talk(ErrandTalk, TalkTarget)),
    ];

    [Fact]
    public async Task Credit_the_user_on_the_step_and_nobody_else()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests(), doodads: QuestDoodads());
        Assert.True(w.Data.Doodads.TryGet(new DoodadTemplateId(Lever), out DoodadTemplateView? lever));
        var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), w.Clock, NullLogger<DoodadScripts>.Instance);
        var visibility = new DoodadVisibility(scripts);
        var uses = new DoodadUseService(scripts, visibility, w.World, w.Clock, NullLogger<DoodadUseService>.Instance, w.Quests);
        using MapInstance instance = TestMapInstances.Build(w.World, quests: w.Quests, doodadVisibility: visibility, doodadUses: uses);
        var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, Errand), lever!, new Vector3(2f, 0f, 0f), 0f);
        instance.AddDoodad(doodad);

        // A and B in one party, both on the lever step, side by side.
        var p = new PartyTestWorld();
        PartyClient partyA = p.Online(1, "A");
        PartyClient partyB = p.Online(2, "B");
        p.Form(partyA, partyB);
        MapInstanceClient a = MapInstanceClients.Join(instance, partyA.Character);
        MapInstanceClient b = MapInstanceClients.Join(instance, partyB.Character);
        a.Character.Position = Vector3.zero;
        b.Character.Position = new Vector3(1f, 0f, 0f);
        foreach (CharacterEntity character in (CharacterEntity[])[a.Character, b.Character])
        {
            character.Quests.Start(Errand, DateTime.UnixEpoch);
            character.Quests.LogSent = true;
            character.Quests.ClearClientChanges();
        }

        uses.Interact(a.Connection, a.Character, instance, doodad.Guid);

        ActiveQuest user = a.Character.Quests.Get(Errand)!;
        Assert.Equal(1u, user.ProgressOf(ErrandUse));
        Assert.Equal(1, user.Stage);
        ActiveQuest member = b.Character.Quests.Get(Errand)!;
        Assert.Empty(member.Progress);
        Assert.Equal(0, member.Stage);

        w.Quests.FlushClient(a.Connection, a.Character);
        SQuestUpdatePacket update = Assert.Single(a.Read<SQuestUpdatePacket>(NetworkPacketType.SMSG_QUEST_UPDATE));
        Assert.Equal((Errand, QuestUpdateKind.Progress, QuestStateKind.Active, 1), (update.QuestId, update.Kind, update.State, update.Stage));
        w.Quests.FlushClient(b.Connection, b.Character);
        Assert.Empty(b.Read<SQuestUpdatePacket>(NetworkPacketType.SMSG_QUEST_UPDATE));

        // A user without the quest, holding one the lever does not move: unchanged, and the walk allocates nothing.
        QuestService quiet = w.QuietQuests();
        CharacterEntity stranger = TestCharacters.New(3);
        stranger.Quests.Start(Hunt, DateTime.UnixEpoch);
        quiet.DoodadUsed(stranger, lever!.Id);
        long before = ThreadAllocations.BeforeQuietWindow();
        quiet.DoodadUsed(stranger, lever.Id);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Empty(stranger.Quests.Get(Hunt)!.Progress);
        Assert.Equal(CharacterQuestState.Active, stranger.Quests.Get(Hunt)!.State);
    }

    /// <summary>A credit that throws costs the use its credit only: the once-per-instance doodad is still spent, so it cannot be used again.</summary>
    [Fact]
    public async Task Still_spend_the_doodad_when_the_quest_credit_throws()
    {
        QuestTestWorld w = await QuestTestWorld.CreateAsync(Quests(), doodads: QuestDoodads());
        Assert.True(w.Data.Doodads.TryGet(new DoodadTemplateId(Lever), out DoodadTemplateView? lever));
        var log = new TestLog();
        var scripts = new DoodadScripts(new ServiceCollection().BuildServiceProvider(), w.Clock, NullLogger<DoodadScripts>.Instance);
        var visibility = new DoodadVisibility(scripts);
        var uses = new DoodadUseService(scripts, visibility, w.World, w.Clock, new Logger<DoodadUseService>(log), w.ThrowingQuests());
        using MapInstance instance = TestMapInstances.Build(w.World, quests: w.Quests, doodadVisibility: visibility, doodadUses: uses);
        var doodad = new Doodad(new ObjectGuid(ObjectType.Doodad, Errand), lever!, new Vector3(2f, 0f, 0f), 0f);
        instance.AddDoodad(doodad);
        MapInstanceClient a = MapInstanceClients.Join(instance, 1);
        a.Character.Position = Vector3.zero;
        a.Character.Quests.Start(Errand, DateTime.UnixEpoch);

        uses.Interact(a.Connection, a.Character, instance, doodad.Guid);

        Assert.Equal(DoodadState.Used, doodad.State);
        Assert.Empty(a.Character.Quests.Get(Errand)!.Progress);
        Assert.Single(log.Errors);
    }
}

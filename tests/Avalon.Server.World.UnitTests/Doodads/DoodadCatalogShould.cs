using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Auras;
using Avalon.Server.World.UnitTests.Loot;
using Avalon.World.Doodads;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>The Doodads reload area: a template that breaks a rule is refused by id, and every other one still loads.</summary>
public class DoodadCatalogShould
{
    private const uint GoodId = 1;
    private const uint BadId = 2;

    private static DoodadTemplate Valid(uint id) => new()
    {
        Id = new DoodadTemplateId(id),
        Name = "Test chest",
        ModelKit = "props-kit",
        ModelKey = "Chest_Wood",
        UsedModelKey = "Chest_Wood_Open",
        ReadyClip = "Chest_Idle",
        UseClip = "Chest_Open",
        UsedClip = "Chest_Opened",
        Interaction = DoodadInteraction.Click,
        InteractionTimeMs = 1500,
        InteractionRange = 5f,
        AreaRadius = 10f,
        UseLimit = DoodadUseLimit.OncePerInstance,
        SpentLook = DoodadSpentLook.ShowUsed,
        RespawnSeconds = 300,
        ScriptName = nameof(TestDoodadScript),
        LootTableId = 1,
        MinGold = 5,
        MaxGold = 20,
        RestorePct = 25,
        UsableQuestId = 1,
        UsableCondition = DoodadGateCondition.OnObjective,
        UsableObjectiveId = 101,
        AuraOutcomes = [new DoodadAuraOutcome { DoodadTemplateId = new DoodadTemplateId(id), AuraId = new AuraId(901), Weight = 3 }],
    };

    /// <summary>A solid template keeps no gate, so only the rule under test can refuse it.</summary>
    private static DoodadTemplate Solid(DoodadTemplate t, float height = 1.2f)
    {
        t.Collision = DoodadCollision.Box;
        t.CollisionHalfX = 0.5f;
        t.CollisionHalfZ = 0.5f;
        t.CollisionHeight = height;
        t.UsableQuestId = null;
        t.UsableCondition = null;
        t.UsableObjectiveId = null;
        return t;
    }

    private static QuestTemplate Quest(uint id, uint objectiveId) => new()
    {
        Id = new QuestTemplateId(id),
        Objectives = [new QuestObjective { Id = objectiveId, QuestId = new QuestTemplateId(id), Type = QuestObjectiveType.Scripted, Count = 1 }],
    };

    private static DoodadCatalog Of(params DoodadTemplate[] templates) =>
        new(templates, LootTestData.Catalog(LootTestData.Table(1, LootTestData.Item(1, LootTestData.Potion))),
            [AuraTestData.Bleed()], [Quest(1, 101), Quest(2, 201)],
            name => name == nameof(TestDoodadScript) ? typeof(TestDoodadScript) : null, NullLoggerFactory.Instance);

    private static DoodadTemplate Mutate(string mutation)
    {
        DoodadTemplate t = Valid(BadId);
        switch (mutation)
        {
            case "model ../x": t.ModelKey = "../x"; break;
            case "used model a/b": t.UsedModelKey = "a/b"; break;
            case "clip x:y": t.UseClip = "x:y"; break;
            case "kit .kit": t.ModelKit = ".kit"; break;
            case "range NaN": t.InteractionRange = float.NaN; break;
            case "range 0": t.InteractionRange = 0f; break;
            case "radius -1": t.AreaRadius = -1f; break;
            case "interaction 120000 ms": t.InteractionTimeMs = 120000; break;
            case "solid with visible gate":
                Solid(t);
                t.VisibleQuestId = 1;
                t.VisibleCondition = DoodadGateCondition.NotCompleted;
                break;
            case "solid with usable gate":
                Solid(t);
                t.UsableQuestId = 1;
                t.UsableCondition = DoodadGateCondition.Completed;
                break;
            case "solid that despawns": Solid(t).SpentLook = DoodadSpentLook.Despawn; break;
            case "solid box 1.0 high": Solid(t, height: 1.0f); break;
            case "gate quest missing": t.UsableQuestId = 99; break;
            case "on objective of another quest": t.UsableObjectiveId = 201; break;
            case "not started with objective": t.UsableCondition = DoodadGateCondition.NotStarted; break;
            case "unknown script": t.ScriptName = "NoSuchScript"; break;
            case "missing loot table": t.LootTableId = 99; break;
            case "outcome aura missing": t.AuraOutcomes[0].AuraId = new AuraId(999); break;
            case "respawn 0": t.RespawnSeconds = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }

        return t;
    }

    [Theory]
    [InlineData("model ../x")]
    [InlineData("used model a/b")]
    [InlineData("clip x:y")]
    [InlineData("kit .kit")]
    [InlineData("range NaN")]
    [InlineData("range 0")]
    [InlineData("radius -1")]
    [InlineData("interaction 120000 ms")]
    [InlineData("solid with visible gate")]
    [InlineData("solid with usable gate")]
    [InlineData("solid that despawns")]
    [InlineData("solid box 1.0 high")]
    [InlineData("gate quest missing")]
    [InlineData("on objective of another quest")]
    [InlineData("not started with objective")]
    [InlineData("unknown script")]
    [InlineData("missing loot table")]
    [InlineData("outcome aura missing")]
    [InlineData("respawn 0")]
    public void Refuse_a_template_that_breaks_a_rule(string mutation)
    {
        DoodadCatalog catalog = Of(Valid(GoodId), Mutate(mutation));

        Assert.False(catalog.TryGet(new DoodadTemplateId(BadId), out _));
        Assert.Single(catalog.Refused, r => r.TemplateId == BadId);
        Assert.True(catalog.TryGet(new DoodadTemplateId(GoodId), out _));
    }

    /// <summary>The 1.05 m solid-top rule is met by a box stored as exactly 1.05f; the view carries what the rules read.</summary>
    [Fact]
    public void Load_a_solid_box_exactly_1_05_m_tall_with_its_gates_and_outcomes()
    {
        DoodadCatalog catalog = Of(Solid(Valid(BadId), height: 1.05f), Valid(GoodId));

        Assert.Empty(catalog.Refused);
        Assert.True(catalog.TryGet(new DoodadTemplateId(BadId), out DoodadTemplateView? solid));
        Assert.True(solid.Solid);
        Assert.Null(solid.Usable);
        Assert.True(catalog.TryGet(new DoodadTemplateId(GoodId), out DoodadTemplateView? chest));
        Assert.False(chest.Solid);
        Assert.Equal(new DoodadGate(1, DoodadGateCondition.OnObjective, 101), chest.Usable);
        Assert.Equal(typeof(TestDoodadScript), chest.ScriptType);
        Assert.Equal((901u, 3u), (Assert.Single(chest.AuraOutcomes).AuraId.Value, chest.OutcomeWeightTotal));
        Assert.Equal([GoodId, BadId], catalog.Templates.Select(t => t.Id.Value));
    }
}

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

    // The template a refusal row breaks, and the second template of a load that refuses nothing.
    private const uint OtherId = 2;

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
        DoodadTemplate t = Valid(OtherId);
        switch (mutation)
        {
            case "id 0": t.Id = new DoodadTemplateId(0); break;
            case "blank name": t.Name = "  "; break;
            case "unknown interaction": t.Interaction = (DoodadInteraction)9; break;
            case "unknown use limit": t.UseLimit = (DoodadUseLimit)9; break;
            case "unknown spent look": t.SpentLook = (DoodadSpentLook)9; break;
            case "unknown collision": t.Collision = (DoodadCollision)9; break;
            case "half-set gate": t.VisibleQuestId = 1; break;
            case "on objective without objective": t.UsableObjectiveId = null; break;
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
            case "min gold above max": t.MinGold = 30; break;
            case "restore 101%": t.RestorePct = 101; break;
            case "outcome weight 0": t.AuraOutcomes[0].Weight = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
        }

        return t;
    }

    /// <summary>One row per rule of the catalog's list, each refused for that rule and no other, by its reason.</summary>
    [Theory]
    [InlineData("id 0", "its id is 0")]
    [InlineData("blank name", "its name is blank")]
    [InlineData("model ../x", "ModelKey '../x' is not a plain asset name")]
    [InlineData("used model a/b", "UsedModelKey 'a/b' is not a plain asset name")]
    [InlineData("clip x:y", "UseClip 'x:y' is not a plain asset name")]
    [InlineData("kit .kit", "ModelKit '.kit' is not a plain asset name")]
    [InlineData("unknown interaction", "Interaction 9 is unknown")]
    [InlineData("unknown use limit", "UseLimit 9 is unknown")]
    [InlineData("unknown spent look", "SpentLook 9 is unknown")]
    [InlineData("unknown collision", "Collision 9 is unknown")]
    [InlineData("half-set gate", "its visible gate needs both a quest and a condition")]
    [InlineData("interaction 120000 ms", "InteractionTimeMs 120000 is above 60000")]
    [InlineData("range NaN", "InteractionRange")]
    [InlineData("range 0", "InteractionRange 0 is not above 0")]
    [InlineData("radius -1", "is not from 0 to")]
    [InlineData("solid box 1.0 high", "needs finite half extents above 0 and a height of at least")]
    [InlineData("solid with visible gate", "a solid doodad cannot be gated")]
    [InlineData("solid with usable gate", "a solid doodad cannot be gated")]
    [InlineData("solid that despawns", "a solid doodad cannot despawn")]
    [InlineData("respawn 0", "RespawnSeconds is 0")]
    [InlineData("gate quest missing", "its usable gate names quest 99, which is not loaded")]
    [InlineData("on objective without objective", "its usable gate is OnObjective but names no objective")]
    [InlineData("on objective of another quest", "its usable gate names objective 201, which is not one of quest 1's")]
    [InlineData("not started with objective", "its usable gate is NotStarted but names objective 101")]
    [InlineData("unknown script", "names doodad script 'NoSuchScript', which is not loaded")]
    [InlineData("missing loot table", "names loot table 99, which is not loaded")]
    [InlineData("min gold above max", "MinGold 30 is above MaxGold 20")]
    [InlineData("restore 101%", "RestorePct 101 is above 100")]
    [InlineData("outcome aura missing", "an outcome names aura 999, which is not loaded")]
    [InlineData("outcome weight 0", "the outcome of aura 901 has weight 0")]
    public void Refuse_a_template_that_breaks_a_rule(string mutation, string reason)
    {
        DoodadTemplate broken = Mutate(mutation);
        DoodadCatalog catalog = Of(Valid(GoodId), broken);

        Assert.False(catalog.TryGet(new DoodadTemplateId(OtherId), out _));
        DoodadRefusal refusal = Assert.Single(catalog.Refused);
        Assert.Equal(broken.Id.Value, refusal.TemplateId);
        Assert.Contains(reason, refusal.Reason, StringComparison.Ordinal);
        Assert.True(catalog.TryGet(new DoodadTemplateId(GoodId), out _));
    }

    /// <summary>The 1.05 m solid-top rule is met by a box stored as exactly 1.05f; the view carries what the rules read.</summary>
    [Fact]
    public void Load_a_solid_box_exactly_1_05_m_tall_with_its_gates_and_outcomes()
    {
        DoodadCatalog catalog = Of(Solid(Valid(OtherId), height: 1.05f), Valid(GoodId));

        Assert.Empty(catalog.Refused);
        Assert.True(catalog.TryGet(new DoodadTemplateId(OtherId), out DoodadTemplateView? solid));
        Assert.True(solid.Solid);
        Assert.Null(solid.Usable);
        Assert.True(catalog.TryGet(new DoodadTemplateId(GoodId), out DoodadTemplateView? chest));
        Assert.False(chest.Solid);
        Assert.Equal(new DoodadGate(1, DoodadGateCondition.OnObjective, 101), chest.Usable);
        Assert.Equal(typeof(TestDoodadScript), chest.ScriptType);
        Assert.Equal((901u, 3u), (Assert.Single(chest.AuraOutcomes).AuraId.Value, chest.OutcomeWeightTotal));
        Assert.Equal([GoodId, OtherId], catalog.Templates.Select(t => t.Id.Value));
    }
}

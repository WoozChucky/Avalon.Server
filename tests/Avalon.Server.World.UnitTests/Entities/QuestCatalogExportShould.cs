using System.Text.Json;
using Avalon.Database.World;
using Avalon.Domain.World;
using Avalon.Exporter;
using Avalon.Server.World.UnitTests.Handlers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Server.World.UnitTests.Entities;

/// <summary>
/// #714: the quest catalog for tooling. Rendering is pure and tested here over the seeded storyline; reading it is
/// EF and is not.
/// </summary>
public class QuestCatalogExportShould
{
    private static (List<QuestTemplate> Quests, Dictionary<int, string> Texts) Seeded()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();
        List<QuestTemplate> quests = context.QuestTemplates.AsNoTracking()
            .Include(q => q.Stages).Include(q => q.Objectives).Include(q => q.ItemRewards).ToList();
        return (quests, context.LocalizedTexts.AsNoTracking().ToDictionary(t => t.Id.Value, t => t.Text));
    }

    private static List<JsonElement> Quests(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("quests").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static List<JsonElement> SeededQuests()
    {
        (List<QuestTemplate> quests, Dictionary<int, string> texts) = Seeded();
        return Quests(QuestCatalogExport.Render(quests, texts));
    }

    [Fact]
    public void List_every_quest_in_id_order_with_its_title()
    {
        List<JsonElement> quests = SeededQuests();

        Assert.Equal([1, 2, 3], quests.Select(q => q.GetProperty("id").GetInt32()));
        Assert.Equal(["Thinning the Herd", "Tusks for Borin", "The Alpha's Howl"],
            quests.Select(q => q.GetProperty("title").GetString()));
    }

    /// <summary>"The Alpha's Howl": three stages in order, each with its objectives' type, target and count.</summary>
    [Fact]
    public void Carry_the_stages_and_objectives_in_order()
    {
        JsonElement alpha = SeededQuests().Single(q => q.GetProperty("id").GetInt32() == 3);

        List<JsonElement> stages = alpha.GetProperty("stages").EnumerateArray().ToList();
        Assert.Equal([0, 1, 2], stages.Select(s => s.GetProperty("sequence").GetInt32()));
        Assert.Equal([(301, 1, 5L, 3), (302, 1, 7L, 2)], Objectives(stages[0]));
        Assert.Equal([(303, 3, 11L, 1)], Objectives(stages[1]));   // talk to Marta
        Assert.Equal([(304, 1, 8L, 1)], Objectives(stages[2]));
    }

    [Fact]
    public void Name_a_collect_objectives_item_as_its_target()
    {
        JsonElement tusks = SeededQuests().Single(q => q.GetProperty("id").GetInt32() == 2);

        Assert.Equal([(201, 2, 57L, 4)], Objectives(tusks.GetProperty("stages")[0]));
    }

    [Fact]
    public void Carry_the_rewards()
    {
        List<JsonElement> quests = SeededQuests();
        JsonElement rewards = quests.Single(q => q.GetProperty("id").GetInt32() == 2).GetProperty("rewards");

        Assert.Equal((250, 150L), (rewards.GetProperty("experience").GetInt32(), rewards.GetProperty("money").GetInt64()));
        Assert.Equal([(56L, 2)], rewards.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("itemTemplateId").GetInt64(), i.GetProperty("count").GetInt32())));
        Assert.Empty(quests.Single(q => q.GetProperty("id").GetInt32() == 1).GetProperty("rewards").GetProperty("items").EnumerateArray());
    }

    /// <summary>The script, requirements and drops are server-only; the file carries the outline and nothing else.</summary>
    [Fact]
    public void Carry_the_outline_and_nothing_else()
    {
        JsonElement quest = SeededQuests()[0];
        JsonElement objective = quest.GetProperty("stages")[0].GetProperty("objectives")[0];

        Assert.Equal(["id", "title", "stages", "rewards"], quest.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["sequence", "objectives"], quest.GetProperty("stages")[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(["id", "type", "targetId", "count"], objective.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["experience", "money", "items"], quest.GetProperty("rewards").EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void Write_an_empty_title_for_a_text_with_no_row()
    {
        (List<QuestTemplate> quests, _) = Seeded();

        Assert.All(Quests(QuestCatalogExport.Render(quests, new Dictionary<int, string>())),
            q => Assert.Equal("", q.GetProperty("title").GetString()));
    }

    /// <summary>
    /// The committed file is the seed rendered: a seed change without a re-export
    /// (<c>tools/Avalon.Exporter -- quest-catalog</c>) fails here.
    /// </summary>
    [Fact]
    public void Match_the_committed_catalog()
    {
        (List<QuestTemplate> quests, Dictionary<int, string> texts) = Seeded();
        string committed = File.ReadAllText(Path.Combine(RepositoryRoot(), "schema", QuestCatalogExport.DirectoryName,
            QuestCatalogExport.FileName));

        Assert.Equal(Lf(committed), Lf(QuestCatalogExport.Render(quests, texts)));
    }

    [Fact]
    public void Render_an_empty_catalog_as_an_empty_array() =>
        Assert.Empty(Quests(QuestCatalogExport.Render([], new Dictionary<int, string>())));

    private static List<(int Id, int Type, long Target, int Count)> Objectives(JsonElement stage) =>
        stage.GetProperty("objectives").EnumerateArray()
            .Select(o => (o.GetProperty("id").GetInt32(), o.GetProperty("type").GetInt32(),
                o.GetProperty("targetId").GetInt64(), o.GetProperty("count").GetInt32()))
            .ToList();

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Avalon.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No Avalon.sln above the test output.");
    }
}

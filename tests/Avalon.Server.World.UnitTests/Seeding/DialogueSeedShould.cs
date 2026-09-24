using Avalon.Database.World;
using Avalon.Domain.Auth;
using Avalon.Domain.World;
using Avalon.Server.World.UnitTests.Handlers;
using Avalon.World.Public.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Avalon.Server.World.UnitTests.Seeding;

/// <summary>
/// Cross-checks over the seeded dialogue graph, against a real SQLite database created from the
/// model — so these assert what ships, not what was intended. Dialogue content is the kind of data
/// where a dangling id or a missing translation is invisible until a player hits it, in one
/// language, in production.
/// </summary>
public class DialogueSeedShould
{
    [Fact]
    public void Point_Every_Node_At_A_Real_Creature_Template_And_A_Real_String()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<DialogueNode> nodes = context.DialogueNodes.AsNoTracking().ToList();
        Assert.NotEmpty(nodes);

        HashSet<ulong> templates = context.CreatureTemplates.AsNoTracking().ToList()
            .Select(t => t.Id.Value).ToHashSet();
        HashSet<int> texts = context.LocalizedTexts.AsNoTracking().ToList()
            .Select(t => t.Id.Value).ToHashSet();

        foreach (DialogueNode node in nodes)
        {
            Assert.True(templates.Contains(node.CreatureTemplateId.Value),
                $"dialogue node {node.Id.Value} belongs to creature template "
                + $"{node.CreatureTemplateId.Value}, which is not seeded");
            Assert.True(texts.Contains(node.TextId.Value),
                $"dialogue node {node.Id.Value} references text {node.TextId.Value}, which is not seeded");
        }
    }

    [Fact]
    public void Give_Every_Node_At_Least_One_Option_So_A_Player_Can_Always_Leave()
    {
        // A node with no options leaves the client showing text it cannot dismiss — a soft-lock
        // the server cannot detect and the player cannot escape.
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<int> nodeIds = context.DialogueNodes.AsNoTracking().ToList()
            .Select(n => n.Id.Value).ToList();
        HashSet<int> nodesWithOptions = context.DialogueOptions.AsNoTracking().ToList()
            .Select(o => o.NodeId.Value).ToHashSet();

        List<int> dangling = nodeIds.Where(id => !nodesWithOptions.Contains(id)).ToList();

        Assert.True(dangling.Count == 0,
            "these dialogue nodes offer no options, so a player cannot dismiss them: "
            + string.Join(", ", dangling));
    }

    [Fact]
    public void Resolve_Every_Option_Target_And_Text()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<DialogueOption> options = context.DialogueOptions.AsNoTracking().ToList();
        Assert.NotEmpty(options);

        HashSet<int> nodeIds = context.DialogueNodes.AsNoTracking().ToList()
            .Select(n => n.Id.Value).ToHashSet();
        HashSet<int> texts = context.LocalizedTexts.AsNoTracking().ToList()
            .Select(t => t.Id.Value).ToHashSet();

        foreach (DialogueOption option in options)
        {
            Assert.True(texts.Contains(option.TextId.Value),
                $"option {option.Id.Value} references text {option.TextId.Value}, which is not seeded");

            if (option.NextNodeId is { } next)
            {
                Assert.True(nodeIds.Contains(next.Value),
                    $"option {option.Id.Value} leads to node {next.Value}, which is not seeded");
            }
        }
    }

    [Fact]
    public void Give_Exactly_One_Root_To_Every_Creature_That_Talks()
    {
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        var byTemplate = context.DialogueNodes.AsNoTracking().ToList()
            .GroupBy(n => n.CreatureTemplateId.Value);

        foreach (var group in byTemplate)
        {
            int roots = group.Count(n => n.IsRoot);
            Assert.True(roots == 1,
                $"creature template {group.Key} has {roots} root dialogue nodes; it needs exactly one");
        }
    }

    [Fact]
    public void Leave_No_Node_Unreachable_From_Its_Root()
    {
        // Orphan content is invisible: it ships, costs translation effort, and no player sees it.
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        List<DialogueNode> nodes = context.DialogueNodes.AsNoTracking().ToList();
        List<DialogueOption> options = context.DialogueOptions.AsNoTracking().ToList();

        var reachable = new HashSet<int>();
        var queue = new Queue<int>(nodes.Where(n => n.IsRoot).Select(n => n.Id.Value));

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!reachable.Add(current)) continue;

            foreach (DialogueOption option in options.Where(o => o.NodeId.Value == current))
            {
                if (option.NextNodeId is { } next) queue.Enqueue(next.Value);
            }
        }

        List<int> orphans = nodes.Select(n => n.Id.Value).Where(id => !reachable.Contains(id)).ToList();

        Assert.True(orphans.Count == 0,
            "these dialogue nodes cannot be reached from any root: " + string.Join(", ", orphans));
    }

    [Fact]
    public void Name_Every_Character_Class()
    {
        // A class with no name row renders {class} as nothing at all.
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        HashSet<CharacterClass> named = context.CharacterClassNames.AsNoTracking().ToList()
            .Select(n => n.Class).ToHashSet();

        List<CharacterClass> missing = Enum.GetValues<CharacterClass>()
            .Where(c => !named.Contains(c)).ToList();

        Assert.True(missing.Count == 0,
            "these classes have no display name, so {class} renders empty for them: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void Translate_Every_String_Into_ptPT()
    {
        // ptPT is the seeded account's locale. Seeding it fully means the translation path runs
        // against shipped data rather than only against test fixtures.
        using SqliteDatabase<WorldDbContext> database = SqliteDatabase.World();
        using WorldDbContext context = database.CreateDbContext();

        HashSet<int> all = context.LocalizedTexts.AsNoTracking().ToList()
            .Select(t => t.Id.Value).ToHashSet();
        HashSet<int> translated = context.LocalizedTextLocales.AsNoTracking().ToList()
            .Where(l => l.Locale == AccountLocale.ptPT)
            .Select(l => l.TextId.Value).ToHashSet();

        List<int> untranslated = all.Where(id => !translated.Contains(id)).ToList();

        Assert.True(untranslated.Count == 0,
            "these strings have no ptPT translation: " + string.Join(", ", untranslated));
    }
}

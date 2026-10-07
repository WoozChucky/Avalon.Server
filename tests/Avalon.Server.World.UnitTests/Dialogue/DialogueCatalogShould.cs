using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Dialogue;
using Avalon.World.Public.Dialogue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Dialogue;

public class DialogueCatalogShould
{
    [Fact]
    public void Pick_The_Lowest_Id_Root_Node_Of_A_Creature()
    {
        // The non-root node deliberately carries the LOWEST id and is listed first, so a catalog that
        // dropped the IsRoot filter would pick it by the id tie-break alone: this proves IsRoot
        // filtering happens, independently of the tie-break among whatever survives it. The two roots
        // are listed highest-id-first: TryAdd is first-write-wins, so without ordering roots by id
        // first, "wins" would mean "whatever order the (unordered) database read happened to hand them
        // in", not "lowest id".
        IDialogueCatalog catalog = Catalog(
            nodes:
            [
                Node(1, creature: 3, root: false, text: 7),
                Node(3, creature: 3, root: true, text: 8),
                Node(2, creature: 3, root: true, text: 6)
            ],
            options: []);

        DialogueNodeView? root = catalog.GetRoot(new CreatureTemplateId(3));

        Assert.NotNull(root);
        Assert.Equal(2, root!.Id.Value);
    }

    [Fact]
    public void Order_Options_By_Sort_Order_Then_By_Id()
    {
        // Listed so that neither key alone gives the right order: SortOrder puts 2 last, and 3 and 1
        // share SortOrder 0, listed highest-id-first. OrderBy is a stable sort, so without a tie-break
        // on id, ties would come back in whatever order the (unordered) database read happened to
        // hand them in — here, 3 before 1.
        IDialogueCatalog catalog = Catalog(
            nodes: [Node(1, creature: 3, root: true, text: 6)],
            options:
            [
                Option(3, node: 1, text: 10, next: null, sort: 0),
                Option(2, node: 1, text: 10, next: null, sort: 1),
                Option(1, node: 1, text: 7, next: null, sort: 0)
            ]);

        DialogueNodeView root = catalog.GetRoot(new CreatureTemplateId(3))!;

        Assert.Equal([1, 3, 2], root.Options.Select(o => o.Id.Value).ToArray());
    }

    [Fact]
    public void Warn_About_An_Option_Whose_Node_Does_Not_Exist()
    {
        // Content error; must not throw at load, because load happens at startup — but a content
        // author who mistyped a NodeId needs to hear about it, once, at load rather than per lookup.
        ILogger innerLogger = Substitute.For<ILogger>();
        ILoggerFactory loggerFactory = Substitute.For<ILoggerFactory>();
        loggerFactory.CreateLogger(Arg.Any<string>()).Returns(innerLogger);

        var catalog = new DialogueCatalog(
            nodes: [Node(1, creature: 3, root: true, text: 6)],
            options: [Option(1, node: 99, text: 10, next: null, sort: 0)],
            loggerFactory);

        // The orphaned option must not silently attach itself to some other node.
        Assert.Empty(catalog.GetRoot(new CreatureTemplateId(3))!.Options);

        int warnings = innerLogger.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(ILogger.Log)
                && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning);

        Assert.Equal(1, warnings);
    }

    private static DialogueNode Node(int id, ulong creature, bool root, int text) => new()
    {
        Id = new DialogueNodeId(id),
        CreatureTemplateId = new CreatureTemplateId(creature),
        IsRoot = root,
        TextId = new LocalizedTextId(text)
    };

    private static DialogueOption Option(int id, int node, int text, int? next, short sort) => new()
    {
        Id = new DialogueOptionId(id),
        NodeId = new DialogueNodeId(node),
        TextId = new LocalizedTextId(text),
        NextNodeId = next is null ? null : new DialogueNodeId(next.Value),
        SortOrder = sort
    };

    private static DialogueCatalog Catalog(
        IReadOnlyCollection<DialogueNode> nodes,
        IReadOnlyCollection<DialogueOption> options)
        => new(nodes, options, NullLoggerFactory.Instance);
}

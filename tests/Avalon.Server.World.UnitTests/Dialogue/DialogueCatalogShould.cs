using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Dialogue;
using Avalon.World.Public.Dialogue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.Dialogue;

public class DialogueCatalogShould
{
    [Fact]
    public void Find_The_Root_Node_For_A_Creature_That_Talks()
    {
        // The non-root node is listed first deliberately: a catalog that picks whichever node it
        // sees first for a creature (instead of filtering on IsRoot) would return node 2 here, not
        // node 1, so this only proves root selection when the root is not simply "first in input".
        IDialogueCatalog catalog = Catalog(
            nodes: [Node(2, creature: 3, root: false, text: 7), Node(1, creature: 3, root: true, text: 6)],
            options: []);

        DialogueNodeView? root = catalog.GetRoot(new CreatureTemplateId(3));

        Assert.NotNull(root);
        Assert.Equal(1, root!.Id.Value);
    }

    [Fact]
    public void Return_Null_For_A_Creature_With_No_Dialogue()
    {
        // Every monster in the game takes this path on interact, so it is the ordinary case.
        IDialogueCatalog catalog = Catalog(nodes: [Node(1, creature: 3, root: true, text: 6)], options: []);

        Assert.Null(catalog.GetRoot(new CreatureTemplateId(4)));
    }

    [Fact]
    public void Return_Null_For_An_Unknown_Node_Id()
    {
        IDialogueCatalog catalog = Catalog(nodes: [Node(1, creature: 3, root: true, text: 6)], options: []);

        Assert.Null(catalog.GetNode(new DialogueNodeId(99)));
    }

    [Fact]
    public void Order_Options_By_Sort_Order()
    {
        IDialogueCatalog catalog = Catalog(
            nodes: [Node(1, creature: 3, root: true, text: 6)],
            options:
            [
                Option(2, node: 1, text: 10, next: null, sort: 1),
                Option(1, node: 1, text: 7, next: 2, sort: 0)
            ]);

        DialogueNodeView root = catalog.GetRoot(new CreatureTemplateId(3))!;

        Assert.Equal([1, 2], root.Options.Select(o => o.Id.Value).ToArray());
    }

    [Fact]
    public void Attach_Only_The_Options_Belonging_To_A_Node()
    {
        IDialogueCatalog catalog = Catalog(
            nodes: [Node(1, creature: 3, root: true, text: 6), Node(2, creature: 3, root: false, text: 7)],
            options:
            [
                Option(1, node: 1, text: 10, next: null, sort: 0),
                Option(2, node: 2, text: 10, next: null, sort: 0)
            ]);

        Assert.Single(catalog.GetRoot(new CreatureTemplateId(3))!.Options);
        Assert.Single(catalog.GetNode(new DialogueNodeId(2))!.Options);
    }

    [Fact]
    public void Carry_The_Creature_Template_On_The_View()
    {
        // The choose handler checks this, so a node cross-linked to the wrong creature cannot be
        // walked by a player talking to a different NPC.
        IDialogueCatalog catalog = Catalog(
            nodes: [Node(1, creature: 3, root: true, text: 6)], options: []);

        Assert.Equal(3ul, catalog.GetRoot(new CreatureTemplateId(3))!.CreatureTemplateId.Value);
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

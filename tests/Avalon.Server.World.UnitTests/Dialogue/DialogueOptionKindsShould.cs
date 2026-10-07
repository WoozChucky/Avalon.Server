using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.Network.Packets.World;
using Avalon.World.Dialogue;
using Avalon.World.Public.Dialogue;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Server.World.UnitTests.Dialogue;

/// <summary>What each dialogue option tells a client it will do (#522).</summary>
public class DialogueOptionKindsShould
{
    private static readonly DialogueNode[] s_nodes =
    [
        new() { Id = 1, CreatureTemplateId = 11, IsRoot = true, TextId = 1 },
        new() { Id = 2, CreatureTemplateId = 11, IsRoot = false, TextId = 2 },
    ];

    private static readonly DialogueOption[] s_options =
    [
        new() { Id = 1, NodeId = 1, TextId = 3, NextNodeId = 2, SortOrder = 0, Action = DialogueOptionAction.OpenBank },
        new() { Id = 2, NodeId = 1, TextId = 3, NextNodeId = 1, SortOrder = 1, Action = DialogueOptionAction.OpenShop },
        new() { Id = 3, NodeId = 1, TextId = 3, NextNodeId = 2, SortOrder = 2 },
        new() { Id = 4, NodeId = 1, TextId = 3, NextNodeId = null, SortOrder = 3, Action = DialogueOptionAction.OpenBank },
        new() { Id = 5, NodeId = 1, TextId = 3, NextNodeId = 99, SortOrder = 4, Action = DialogueOptionAction.OpenShop },
    ];

    private static DialogueOptionKind KindOf(int optionId)
    {
        var catalog = new DialogueCatalog(s_nodes, s_options, NullLoggerFactory.Instance);
        var actions = new DialogueActions(s_nodes, s_options);
        DialogueOptionView option = catalog.GetNode(new DialogueNodeId(1))!.Options.Single(o => o.Id.Value == optionId);
        return DialogueOptionKinds.For(actions, catalog, option);
    }

    /// <summary>
    /// The action an option that keeps the conversation open runs, and a conversation otherwise:
    /// DialogueChooseHandler runs no action on an option that ends the conversation or leads to an
    /// unknown node, so the wire must not promise one.
    /// </summary>
    [Theory]
    [InlineData(1, DialogueOptionKind.OpenBank)]       // every action's kind: Map_every_action_to_a_kind_of_its_own
    [InlineData(3, DialogueOptionKind.Conversation)]   // no action
    [InlineData(4, DialogueOptionKind.Conversation)]   // ends the conversation
    [InlineData(5, DialogueOptionKind.Conversation)]   // leads to an unknown node
    public void Tell_the_client_only_the_action_choosing_the_option_will_run(int optionId, DialogueOptionKind expected)
    {
        Assert.Equal(expected, KindOf(optionId));
    }

    /// <summary>A new action with no wire kind fails here rather than going out as a conversation.</summary>
    [Fact]
    public void Map_every_action_to_a_kind_of_its_own()
    {
        DialogueOptionAction[] all = Enum.GetValues<DialogueOptionAction>();
        DialogueOptionKind[] kinds = all.Select(DialogueOptionKinds.Of).ToArray();

        Assert.DoesNotContain(DialogueOptionKind.Conversation, kinds);
        Assert.Equal(all.Length, kinds.Distinct().Count());
        Assert.Equal(DialogueOptionKind.OpenBank, DialogueOptionKinds.Of(DialogueOptionAction.OpenBank));
        Assert.Equal(DialogueOptionKind.OpenShop, DialogueOptionKinds.Of(DialogueOptionAction.OpenShop));
    }
}

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
    private static readonly DialogueNode[] Nodes =
    [
        new() { Id = 1, CreatureTemplateId = 11, IsRoot = true, TextId = 1 },
        new() { Id = 2, CreatureTemplateId = 11, IsRoot = false, TextId = 2 },
    ];

    private static readonly DialogueOption[] Options =
    [
        new() { Id = 1, NodeId = 1, TextId = 3, NextNodeId = 2, SortOrder = 0, Action = DialogueOptionAction.OpenBank },
        new() { Id = 2, NodeId = 1, TextId = 3, NextNodeId = 1, SortOrder = 1, Action = DialogueOptionAction.OpenShop },
        new() { Id = 3, NodeId = 1, TextId = 3, NextNodeId = 2, SortOrder = 2 },
        new() { Id = 4, NodeId = 1, TextId = 3, NextNodeId = null, SortOrder = 3, Action = DialogueOptionAction.OpenBank },
        new() { Id = 5, NodeId = 1, TextId = 3, NextNodeId = 99, SortOrder = 4, Action = DialogueOptionAction.OpenShop },
    ];

    private static DialogueOptionKind KindOf(int optionId)
    {
        var catalog = new DialogueCatalog(Nodes, Options, NullLoggerFactory.Instance);
        var actions = new DialogueActions(Nodes, Options);
        DialogueOptionView option = catalog.GetNode(new DialogueNodeId(1))!.Options.Single(o => o.Id.Value == optionId);
        return DialogueOptionKinds.For(actions, catalog, option);
    }

    [Fact]
    public void Name_the_action_an_option_that_keeps_the_conversation_open_runs()
    {
        Assert.Equal(DialogueOptionKind.OpenBank, KindOf(1));
        Assert.Equal(DialogueOptionKind.OpenShop, KindOf(2));
    }

    [Fact]
    public void Call_an_option_without_an_action_a_conversation()
    {
        Assert.Equal(DialogueOptionKind.Conversation, KindOf(3));
    }

    /// <summary>DialogueChooseHandler runs no action on these, so the wire must not promise one.</summary>
    [Fact]
    public void Call_an_action_that_would_never_run_a_conversation()
    {
        Assert.Equal(DialogueOptionKind.Conversation, KindOf(4));   // ends the conversation
        Assert.Equal(DialogueOptionKind.Conversation, KindOf(5));   // leads to an unknown node
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

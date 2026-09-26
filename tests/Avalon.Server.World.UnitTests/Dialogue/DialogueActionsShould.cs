using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Dialogue;

namespace Avalon.Server.World.UnitTests.Dialogue;

public class DialogueActionsShould
{
    private static readonly DialogueNode[] Nodes =
    [
        new() { Id = 1, CreatureTemplateId = 11, IsRoot = true, TextId = 1 },
        new() { Id = 2, CreatureTemplateId = 11, IsRoot = false, TextId = 2 },
        new() { Id = 3, CreatureTemplateId = 3, IsRoot = true, TextId = 3 },
    ];

    private static readonly DialogueOption[] Options =
    [
        new() { Id = 1, NodeId = 1, TextId = 4, NextNodeId = 2 },
        new() { Id = 2, NodeId = 2, TextId = 5, NextNodeId = 1, Action = DialogueOptionAction.OpenBank },
        new() { Id = 3, NodeId = 3, TextId = 6, NextNodeId = null },
        new() { Id = 4, NodeId = 99, TextId = 7, Action = DialogueOptionAction.OpenBank },   // unknown node
    ];

    [Fact]
    public void Say_what_an_option_does()
    {
        var actions = new DialogueActions(Nodes, Options);

        Assert.Equal(DialogueOptionAction.OpenBank, actions.For(new DialogueOptionId(2)));
        Assert.Null(actions.For(new DialogueOptionId(1)));
        Assert.Null(actions.For(new DialogueOptionId(77)));
    }

    [Fact]
    public void Offer_an_action_for_the_template_whose_dialogue_holds_it_anywhere()
    {
        var actions = new DialogueActions(Nodes, Options);

        Assert.True(actions.Offers(new CreatureTemplateId(11), DialogueOptionAction.OpenBank));
        Assert.False(actions.Offers(new CreatureTemplateId(3), DialogueOptionAction.OpenBank));
    }

    [Fact]
    public void Ignore_an_option_on_a_node_that_does_not_exist()
    {
        var actions = new DialogueActions(Nodes, Options);

        Assert.Null(actions.For(new DialogueOptionId(4)));
        Assert.Equal(1, actions.Count);
    }

    /// <summary>The Action column stores the number, so the enum is append-only (#463, #432).</summary>
    [Fact]
    public void Keep_the_stored_action_numbers()
    {
        Assert.Equal(0, (int)DialogueOptionAction.OpenBank);
        Assert.Equal(1, (int)DialogueOptionAction.OpenShop);
    }
}

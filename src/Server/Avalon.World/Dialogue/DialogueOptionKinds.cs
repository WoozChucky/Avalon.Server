using Avalon.Domain.World;
using Avalon.Network.Packets.World;
using Avalon.World.Public.Dialogue;
using Avalon.World.Public.Localization;

namespace Avalon.World.Dialogue;

/// <summary>
/// What each dialogue option tells a client it will do (#522): the one rule for which option's
/// action runs, shared by <c>DialogueChooseHandler</c> (which runs it) and by the node every
/// handler sends (which announces it), so the wire cannot promise an action the server would not run.
/// </summary>
public static class DialogueOptionKinds
{
    /// <summary>
    /// The action choosing this option runs, or null when it runs none. An action runs only on an
    /// option that keeps the conversation open, that is one leading to a node that exists: on an
    /// option that ends the conversation (or leads nowhere) the window would close in the same breath.
    /// </summary>
    public static DialogueOptionAction? ActionThatRuns(DialogueActions actions, IDialogueCatalog dialogue, DialogueOptionView option)
        => option.NextNodeId is { } next && dialogue.GetNode(next) is not null ? actions.For(option.Id) : null;

    /// <summary>The wire's name for an action. Explicit, never a cast: the stored numbers differ.</summary>
    public static DialogueOptionKind Of(DialogueOptionAction action) => action switch
    {
        DialogueOptionAction.OpenBank => DialogueOptionKind.OpenBank,
        DialogueOptionAction.OpenShop => DialogueOptionKind.OpenShop,
        // An action this server does not know runs nothing (DialogueChooseHandler logs it).
        _ => DialogueOptionKind.Conversation,
    };

    /// <summary>What the option goes out as: the action it will run, or Conversation.</summary>
    public static DialogueOptionKind For(DialogueActions actions, IDialogueCatalog dialogue, DialogueOptionView option)
        => ActionThatRuns(actions, dialogue, option) is { } action ? Of(action) : DialogueOptionKind.Conversation;

    /// <summary>A node's options as they go on the wire, text resolved and each with its kind.</summary>
    public static List<SDialogueOptionInfo> OptionsOf(
        DialogueNodeView node,
        DialogueActions actions,
        IDialogueCatalog dialogue,
        ILocalizedTextCatalog text,
        TextContext context)
        => node.Options
            .Select(option => new SDialogueOptionInfo
            {
                OptionId = option.Id.Value,
                Text = text.Get(option.TextId, context),
                Kind = For(actions, dialogue, option),
            })
            .ToList();
}

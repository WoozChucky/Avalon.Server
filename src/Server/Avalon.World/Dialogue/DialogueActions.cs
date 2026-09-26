using Avalon.Common.ValueObjects;
using Avalon.Domain.World;

namespace Avalon.World.Dialogue;

/// <summary>
/// What each dialogue option does besides moving the conversation (spec #463). Kept out of the
/// World.Public dialogue view on purpose: an action reaches server state (the bank today, vendors
/// next), and the modding API must not be able to read or invent one. Built in the same patch as
/// the catalog, so /reload dialogue rebuilds both together.
/// </summary>
public sealed class DialogueActions
{
    private readonly Dictionary<int, DialogueOptionAction> _byOption = [];
    private readonly HashSet<(ulong Template, DialogueOptionAction Action)> _offered = [];

    public DialogueActions(IReadOnlyCollection<DialogueNode> nodes, IReadOnlyCollection<DialogueOption> options)
    {
        Dictionary<int, ulong> templateByNode = nodes.ToDictionary(n => n.Id.Value, n => n.CreatureTemplateId.Value);

        foreach (DialogueOption option in options)
        {
            // An option on an unknown node is ignored, as DialogueCatalog ignores it.
            if (option.Action is not { } action || !templateByNode.TryGetValue(option.NodeId.Value, out ulong template))
                continue;

            _byOption[option.Id.Value] = action;
            _offered.Add((template, action));
        }
    }

    /// <summary>How many options carry an action.</summary>
    public int Count => _byOption.Count;

    /// <summary>What choosing the option does, or null when it only talks.</summary>
    public DialogueOptionAction? For(DialogueOptionId optionId) =>
        _byOption.TryGetValue(optionId.Value, out DialogueOptionAction action) ? action : null;

    /// <summary>True when some option anywhere in this creature template's dialogue has the action.</summary>
    public bool Offers(CreatureTemplateId templateId, DialogueOptionAction action) =>
        _offered.Contains((templateId.Value, action));
}

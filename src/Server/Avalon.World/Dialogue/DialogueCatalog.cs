using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.Public.Dialogue;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Dialogue;

public class DialogueCatalog : IDialogueCatalog
{
    private readonly Dictionary<int, DialogueNodeView> _byNode;
    private readonly Dictionary<ulong, DialogueNodeView> _rootByCreature;

    public DialogueCatalog(
        IReadOnlyCollection<DialogueNode> nodes,
        IReadOnlyCollection<DialogueOption> options,
        ILoggerFactory loggerFactory)
    {
        ILogger<DialogueCatalog> logger = loggerFactory.CreateLogger<DialogueCatalog>();

        Dictionary<int, List<DialogueOption>> optionsByNode = [];
        HashSet<int> knownNodes = nodes.Select(n => n.Id.Value).ToHashSet();

        foreach (DialogueOption option in options)
        {
            if (!knownNodes.Contains(option.NodeId.Value))
            {
                logger.LogWarning("Dialogue option {OptionId} belongs to unknown node {NodeId}; ignoring it",
                    option.Id.Value, option.NodeId.Value);
                continue;
            }

            if (!optionsByNode.TryGetValue(option.NodeId.Value, out List<DialogueOption>? list))
            {
                list = [];
                optionsByNode[option.NodeId.Value] = list;
            }

            list.Add(option);
        }

        _byNode = nodes.ToDictionary(
            node => node.Id.Value,
            node => new DialogueNodeView(
                node.Id,
                node.CreatureTemplateId,
                node.TextId,
                optionsByNode.TryGetValue(node.Id.Value, out List<DialogueOption>? found)
                    ? found.OrderBy(o => o.SortOrder)
                        .Select(o => new DialogueOptionView(o.Id, o.TextId, o.NextNodeId))
                        .ToList()
                    : []));

        _rootByCreature = [];
        foreach (DialogueNode node in nodes.Where(n => n.IsRoot))
        {
            if (!_rootByCreature.TryAdd(node.CreatureTemplateId.Value, _byNode[node.Id.Value]))
            {
                logger.LogWarning(
                    "Creature template {CreatureId} has more than one root dialogue node; keeping the first",
                    node.CreatureTemplateId.Value);
            }
        }

        logger.LogInformation("Loaded {NodeCount} dialogue nodes for {CreatureCount} creatures",
            _byNode.Count, _rootByCreature.Count);
    }

    public DialogueNodeView? GetRoot(CreatureTemplateId creatureTemplateId)
        => _rootByCreature.GetValueOrDefault(creatureTemplateId.Value);

    public DialogueNodeView? GetNode(DialogueNodeId nodeId)
        => _byNode.GetValueOrDefault(nodeId.Value);
}

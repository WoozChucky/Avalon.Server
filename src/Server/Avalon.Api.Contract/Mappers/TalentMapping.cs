using Avalon.Domain.World;

namespace Avalon.Api.Contract.Mappers;

/// <summary>The talent tree as the REST contract carries it (#1181).</summary>
public static class TalentMapping
{
    /// <summary>Nodes by id, each node's stats by (stat, kind), edges by (NodeA, NodeB) and starts by class.</summary>
    public static TalentTreeDto ToDto(this TalentTreeRows rows)
    {
        ILookup<int, TalentNodeStat> stats = rows.Stats.ToLookup(s => s.NodeId);
        var abilities = rows.Abilities.ToDictionary(a => a.NodeId, a => a.AbilityId.Value);

        return new TalentTreeDto
        {
            Nodes =
            [
                .. rows.Nodes.OrderBy(n => n.Id).Select(n => new TalentNodeDto
                {
                    Id = n.Id,
                    Kind = (TalentNodeKind)n.Kind,
                    Name = n.Name,
                    Angle = n.Angle,
                    Radius = n.Radius,
                    ForkGroupId = n.ForkGroupId,
                    AbilityId = abilities.TryGetValue(n.Id, out uint ability) ? ability : null,
                    Stats =
                    [
                        .. stats[n.Id].OrderBy(s => s.Stat).ThenBy(s => s.Kind).Select(s => new TalentNodeStatDto
                        {
                            Stat = (StatType)s.Stat,
                            Kind = (TalentStatKind)s.Kind,
                            Value = s.Value,
                            ValueMax = s.ValueMax,
                        }),
                    ],
                }),
            ],
            Edges =
            [
                .. rows.Edges.OrderBy(e => e.NodeA).ThenBy(e => e.NodeB)
                    .Select(e => new TalentEdgeDto { NodeA = e.NodeA, NodeB = e.NodeB }),
            ],
            ClassStarts =
            [
                .. rows.Starts.OrderBy(s => s.Class)
                    .Select(s => new TalentClassStartDto { Class = s.Class, NodeId = s.NodeId }),
            ],
        };
    }
}

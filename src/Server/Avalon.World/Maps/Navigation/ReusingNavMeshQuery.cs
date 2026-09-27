using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using static DotRecast.Detour.DtDetour;

namespace Avalon.World.Maps.Navigation;

/// <summary>
/// A <see cref="DtNavMeshQuery" /> whose surface move reuses its search queue (#638).
/// <see cref="DtNavMeshQuery.MoveAlongSurface" /> builds a new <c>LinkedList</c> and a node per
/// polygon it visits on every call, and <see cref="MapNavigator.FindPath" /> calls it once per half
/// metre of route, which made it most of what a re-path allocated once the path lists were reused.
/// </summary>
/// <remarks>
/// <see cref="MoveAlongSurface" /> is DotRecast's <c>DtNavMeshQuery.MoveAlongSurface</c> (zlib licence,
/// copyright Mikko Mononen, Piotr Piastucki and Choi Ikpil), altered only in its queue: the same
/// first-in, first-out order over a list kept by this query, so its results are the original's.
/// Like the query's own node pools, the queue is per-call state, so one query serves one thread at a
/// time.
/// </remarks>
public sealed class ReusingNavMeshQuery : DtNavMeshQuery
{
    private readonly List<DtNode> _queue = [];

    public ReusingNavMeshQuery(DtNavMesh nav) : base(nav)
    {
    }

    public new DtStatus MoveAlongSurface(long startRef, RcVec3f startPos, RcVec3f endPos,
        IDtQueryFilter filter,
        out RcVec3f resultPos, Span<long> visited, out int visitedCount, int maxVisitedSize)
    {
        resultPos = RcVec3f.Zero;

        visitedCount = 0;

        // Validate input
        if (!m_nav.IsValidPolyRef(startRef) || !startPos.IsFinite()
                                            || !endPos.IsFinite() || null == filter)
        {
            return DtStatus.DT_FAILURE | DtStatus.DT_INVALID_PARAM;
        }

        DtStatus status = DtStatus.DT_SUCCESS;

        m_tinyNodePool.Clear();

        DtNode startNode = m_tinyNodePool.GetNode(startRef);
        startNode.pidx = 0;
        startNode.cost = 0;
        startNode.total = 0;
        startNode.id = startRef;
        startNode.flags = DtNodeFlags.DT_NODE_CLOSED;

        // The one alteration: a reused list read from a head index, in place of a new LinkedList.
        _queue.Clear();
        int head = 0;
        _queue.Add(startNode);

        float bestDist = float.MaxValue;
        DtNode? bestNode = null;
        RcVec3f bestPos = startPos;

        // Search constraints
        var searchPos = RcVec3f.Lerp(startPos, endPos, 0.5f);
        float searchRadSqr = RcMath.Sqr(RcVec3f.Distance(startPos, endPos) / 2.0f + 0.001f);

        Span<float> verts = stackalloc float[m_nav.GetMaxVertsPerPoly() * 3];

        const int MaxNeis = 8;
        Span<long> neis = stackalloc long[MaxNeis];

        while (head < _queue.Count)
        {
            // Pop front.
            DtNode curNode = _queue[head++];

            // Get poly and tile.
            // The API input has been checked already, skip checking internal data.
            long curRef = curNode.id;
            m_nav.GetTileAndPolyByRefUnsafe(curRef, out var curTile, out var curPoly);

            // Collect vertices.
            int nverts = curPoly.vertCount;
            for (int i = 0; i < nverts; ++i)
            {
                RcSpans.Copy(curTile.data.verts, curPoly.verts[i] * 3, verts, i * 3, 3);
            }

            // If target is inside the poly, stop search.
            if (DtUtils.PointInPolygon(endPos, verts, nverts))
            {
                bestNode = curNode;
                bestPos = endPos;
                break;
            }

            // Find wall edges and find nearest point inside the walls.
            for (int i = 0, j = curPoly.vertCount - 1; i < curPoly.vertCount; j = i++)
            {
                // Find links to neighbours.
                int nneis = 0;

                if ((curPoly.neis[j] & DT_EXT_LINK) != 0)
                {
                    // Tile border.
                    for (int k = curPoly.firstLink; k != DT_NULL_LINK; k = curTile.links[k].next)
                    {
                        DtLink link = curTile.links[k];
                        if (link.edge == j && link.refs != 0)
                        {
                            m_nav.GetTileAndPolyByRefUnsafe(link.refs, out var neiTile, out var neiPoly);
                            if (filter.PassFilter(link.refs, neiTile, neiPoly) && nneis < MaxNeis)
                            {
                                neis[nneis++] = link.refs;
                            }
                        }
                    }
                }
                else if (curPoly.neis[j] != 0)
                {
                    int idx = curPoly.neis[j] - 1;
                    long refs = m_nav.GetPolyRefBase(curTile) | (long)idx;
                    if (filter.PassFilter(refs, curTile, curTile.data.polys[idx]))
                    {
                        // Internal edge, encode id.
                        neis[nneis++] = refs;
                    }
                }

                if (nneis == 0)
                {
                    // Wall edge, calc distance.
                    int vj = j * 3;
                    int vi = i * 3;
                    var distSqr = DtUtils.DistancePtSegSqr2D(endPos, verts, vj, vi, out var tseg);
                    if (distSqr < bestDist)
                    {
                        // Update nearest distance.
                        bestPos = RcVec.Lerp(verts, vj, vi, tseg);
                        bestDist = distSqr;
                        bestNode = curNode;
                    }
                }
                else
                {
                    for (int k = 0; k < nneis; ++k)
                    {
                        DtNode neighbourNode = m_tinyNodePool.GetNode(neis[k]);
                        // Skip if already visited.
                        if ((neighbourNode.flags & DtNodeFlags.DT_NODE_CLOSED) != 0)
                        {
                            continue;
                        }

                        // Skip the link if it is too far from search constraint.
                        int vj = j * 3;
                        int vi = i * 3;
                        var distSqr = DtUtils.DistancePtSegSqr2D(searchPos, verts, vj, vi, out var _);
                        if (distSqr > searchRadSqr)
                        {
                            continue;
                        }

                        // Mark as the node as visited and push to queue.
                        neighbourNode.pidx = m_tinyNodePool.GetNodeIdx(curNode);
                        neighbourNode.flags |= DtNodeFlags.DT_NODE_CLOSED;
                        _queue.Add(neighbourNode);
                    }
                }
            }
        }

        int n = 0;
        if (bestNode != null)
        {
            // Reverse the path.
            DtNode? prev = null;
            DtNode? node = bestNode;
            do
            {
                DtNode next = m_tinyNodePool.GetNodeAtIdx(node.pidx);
                node.pidx = m_tinyNodePool.GetNodeIdx(prev);
                prev = node;
                node = next;
            } while (node != null);

            // Store result
            node = prev;
            do
            {
                visited[n++] = node!.id;
                if (n >= maxVisitedSize)
                {
                    status |= DtStatus.DT_BUFFER_TOO_SMALL;
                    break;
                }

                node = m_tinyNodePool.GetNodeAtIdx(node.pidx);
            } while (node != null);
        }

        resultPos = bestPos;
        visitedCount = n;

        return status;
    }
}

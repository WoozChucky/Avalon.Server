using Avalon.Common.Mathematics;
using Avalon.Common.Queues;

namespace Avalon.World.Pathfinding;

public class AStarPolygonMap
{
    internal class PathNode : FastPriorityQueueNode
    {
        public int VertexIndex;

        public PathNode(int vertexIndex)
        {
            VertexIndex = vertexIndex;
        }
    }

    public static List<Vector3>? GeneratePath(Vector3 start, Vector3 end, int[] indices, Vector3[] vertices, int[] areas)
    {
        // Find the closest vertex to the start and end positions
        int startVertex = FindClosestVertex(start, vertices);
        int endVertex = FindClosestVertex(end, vertices);

        // Find the path using A* or Dijkstra's algorithm
        var pathIndices = FindPath(startVertex, endVertex, indices, vertices, areas);
        if (pathIndices == null)
        {
            return null; // No path found
        }

        // Convert path indices to path positions
        var pathPositions = pathIndices.Select(index => vertices[index]).ToList();

        // Simplify the path using string pulling
        var simplifiedPath = StringPulling(pathPositions);

        // Smooth the path
        var smoothedPath = SmoothPath(simplifiedPath);

        return smoothedPath;
    }

    private static int FindClosestVertex(Vector3 position, Vector3[] vertices)
    {
        var closestIndex = -1;
        var closestDistanceSqr = float.MaxValue;

        for (var i = 0; i < vertices.Length; i++)
        {
            var distanceSqr = (vertices[i] - position).sqrMagnitude;
            if (distanceSqr < closestDistanceSqr)
            {
                closestIndex = i;
                closestDistanceSqr = distanceSqr;
            }
        }
        return closestIndex;
    }

    private static List<int>? FindPath(int startVertex, int endVertex, int[] indices, Vector3[] vertices, int[] areas)
    {
        var maxNodes = vertices.Length;
        var openSet = new FastPriorityQueue<PathNode>(maxNodes);
        var cameFrom = new Dictionary<int, int>();
        var gScore = new Dictionary<int, float>();
        var fScore = new Dictionary<int, float>();

        var startNode = new PathNode(startVertex);
        openSet.Enqueue(startNode, 0);
        gScore[startVertex] = 0;
        fScore[startVertex] = HeuristicCostEstimate(vertices[startVertex], vertices[endVertex]);

        while (openSet.Count > 0)
        {
            var currentNode = openSet.Dequeue();
            int current = currentNode.VertexIndex;

            if (current == endVertex)
            {
                return ReconstructPath(cameFrom, current);
            }

            foreach (var neighbor in GetNeighbors(current, indices))
            {
                float tentativeGScore = gScore[current] + (vertices[current] - vertices[neighbor]).magnitude;
                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    float priority = gScore[neighbor] + HeuristicCostEstimate(vertices[neighbor], vertices[endVertex]);
                    fScore[neighbor] = priority;

                    if (!openSet.Contains(new PathNode(neighbor)))
                    {
                        openSet.Enqueue(new PathNode(neighbor), priority);
                    }
                    else
                    {
                        openSet.UpdatePriority(new PathNode(neighbor), priority);
                    }
                }
            }
        }

        return null; // Path not found
    }

    private static float HeuristicCostEstimate(Vector3 start, Vector3 goal)
    {
        return (start - goal).magnitude;
    }

    private static List<int> GetNeighbors(int vertex, int[] indices)
    {
        var neighbors = new HashSet<int>();
        const int verticesPerPolygon = 3;

        for (var i = 0; i < indices.Length; i += verticesPerPolygon)
        {
            if (i + 2 < indices.Length) // Ensure we don't go out of bounds
            {
                // Check for triangle
                if (indices[i] == vertex || indices[i + 1] == vertex || indices[i + 2] == vertex)
                {
                    neighbors.Add(indices[i]);
                    neighbors.Add(indices[i + 1]);
                    neighbors.Add(indices[i + 2]);
                }
            }

            if (i + 3 < indices.Length) // Ensure we don't go out of bounds
            {
                // Check for quadrilateral
                if (indices[i] == vertex || indices[i + 1] == vertex || indices[i + 2] == vertex || indices[i + 3] == vertex)
                {
                    neighbors.Add(indices[i]);
                    neighbors.Add(indices[i + 1]);
                    neighbors.Add(indices[i + 2]);
                    neighbors.Add(indices[i + 3]);
                }
            }
        }

        neighbors.Remove(vertex); // Remove the vertex itself from its neighbors
        return neighbors.ToList();
    }

    private static List<int> ReconstructPath(Dictionary<int, int> cameFrom, int current)
    {
        var totalPath = new List<int> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            totalPath.Add(current);
        }
        totalPath.Reverse();
        return totalPath;
    }

    private static List<Vector3> StringPulling(List<Vector3> path)
    {
        if (path.Count < 2)
        {
            return path;
        }

        List<Vector3> result = new List<Vector3>();
        Vector3 apex = path[0];
        Vector3 left = path[1];
        Vector3 right = path[1];

        result.Add(apex);

        for (int i = 2; i < path.Count; i++)
        {
            Vector3 p = path[i];

            // Update funnel
            if (IsLeft(apex, right, p))
            {
                if (apex == right || IsLeft(apex, left, p))
                {
                    right = p;
                }
                else
                {
                    result.Add(left);
                    apex = left;
                    left = apex;
                    right = p;
                }
            }

            if (!IsLeft(apex, left, p))
            {
                if (apex == left || !IsLeft(apex, right, p))
                {
                    left = p;
                }
                else
                {
                    result.Add(right);
                    apex = right;
                    right = apex;
                    left = p;
                }
            }
        }

        result.Add(path[path.Count - 1]);

        return result;
    }

    private static bool IsLeft(Vector3 a, Vector3 b, Vector3 c)
    {
        return ((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x)) >= 0;
    }

    private static List<Vector3> SmoothPath(List<Vector3> path)
    {
        if (path.Count < 3)
        {
            return path;
        }

        List<Vector3> smoothPath = new List<Vector3>();
        smoothPath.Add(path[0]);

        for (int i = 0; i < path.Count - 2; i += 2)
        {
            Vector3 p0 = path[i];
            Vector3 p1 = path[i + 1];
            Vector3 p2 = path[i + 2];

            // Add intermediate points using quadratic Bezier interpolation
            for (float t = 0; t <= 1; t += 0.1f)
            {
                Vector3 point = QuadraticBezier(p0, p1, p2, t);
                smoothPath.Add(point);
            }
        }

        smoothPath.Add(path[^1]);
        return smoothPath;
    }

    private static Vector3 QuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
    {
        float u = 1 - t;
        return (u * u) * p0 + (2 * u * t) * p1 + (t * t) * p2;
    }
}

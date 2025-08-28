using Avalon.Common.Mathematics;
using Avalon.World.Maps.Virtualized;
using Avalon.World.Pathfinding;
using Avalon.World.Public.Maps;
using Xunit;

namespace Avalon.Server.IntegrationTests.World;

public class ChunkShould
{
    private Vector3[] GenerateVertices()
    {
        Vector3[] vertices = new Vector3[25];
        int index = 0;
        for (int x = 0; x <= 40; x += 10)
        {
            for (int z = 0; z <= 40; z += 10)
            {
                vertices[index++] = new Vector3(x, 0, z);
            }
        }

        return vertices;
    }

    private int[] GenerateIndices()
    {
        int gridSize = 5;
        List<int> indices = new List<int>();
        for (int x = 0; x < gridSize - 1; x++)
        {
            for (int z = 0; z < gridSize - 1; z++)
            {
                int topLeft = x * gridSize + z;
                int topRight = topLeft + 1;
                int bottomLeft = (x + 1) * gridSize + z;
                int bottomRight = bottomLeft + 1;

                indices.Add(topLeft);
                indices.Add(bottomLeft);
                indices.Add(topRight);

                indices.Add(topRight);
                indices.Add(bottomLeft);
                indices.Add(bottomRight);
            }
        }

        return indices.ToArray();
    }


    [Fact]
    public void Test()
    {
        // Arrange
        var start = new Vector3(5, 0, 5);
        var end = new Vector3(35, 0, 35);

        var chunk = new ChunkMetadata
        {
            Name = "Test Chunk",
            Position = Vector3.zero,
            Size = new Vector3(50, 1, 50),
            Trees = new List<TreeInfo>(),
            Creatures = new List<CreatureInfo>(),
        };


    }
}

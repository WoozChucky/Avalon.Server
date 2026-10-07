using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public.Enums;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

public class ChunkLayoutSourceResolverShould
{
    private static MapTemplate Template(MapType type) => new()
    {
        Id = new MapTemplateId(1),
        MapType = type,
        Name = "test"
    };

    [Theory]
    [InlineData(MapType.Town, ChunkLayoutSourceKind.Predefined)]
    [InlineData(MapType.Normal, ChunkLayoutSourceKind.Procedural)]
    public void Return_the_source_for_the_maps_type(MapType type, ChunkLayoutSourceKind expected)
    {
        IChunkLayoutSource predefined = Substitute.For<IChunkLayoutSource>();
        IChunkLayoutSource procedural = Substitute.For<IChunkLayoutSource>();
        var resolver = new ChunkLayoutSourceResolver(predefined, procedural);

        IChunkLayoutSource result = resolver.Resolve(Template(type), out ChunkLayoutSourceKind kind);

        Assert.Same(expected == ChunkLayoutSourceKind.Predefined ? predefined : procedural, result);
        Assert.Equal(expected, kind);
    }
}

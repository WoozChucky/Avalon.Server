using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Public.Enums;
using NSubstitute;
using Xunit;

namespace Avalon.Server.World.UnitTests.ChunkLayouts;

public class ChunkLayoutSourceResolverShould
{
    private static MapTemplate Template(MapType type) => new()
    {
        Id = new MapTemplateId(1),
        MapType = type,
        Name = "test"
    };

    [Fact]
    public void Return_predefined_source_for_town_maps()
    {
        var predefined = Substitute.For<IChunkLayoutSource>();
        var procedural = Substitute.For<IChunkLayoutSource>();
        var resolver = new ChunkLayoutSourceResolver(predefined, procedural);

        var result = resolver.Resolve(Template(MapType.Town), out var kind);

        Assert.Same(predefined, result);
        Assert.Equal(ChunkLayoutSourceKind.Predefined, kind);
    }

    [Fact]
    public void Return_procedural_source_for_normal_maps()
    {
        var predefined = Substitute.For<IChunkLayoutSource>();
        var procedural = Substitute.For<IChunkLayoutSource>();
        var resolver = new ChunkLayoutSourceResolver(predefined, procedural);

        var result = resolver.Resolve(Template(MapType.Normal), out var kind);

        Assert.Same(procedural, result);
        Assert.Equal(ChunkLayoutSourceKind.Procedural, kind);
    }
}

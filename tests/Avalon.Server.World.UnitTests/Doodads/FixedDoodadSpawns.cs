using Avalon.Common.ValueObjects;
using Avalon.Database.World.Repositories;
using Avalon.Domain.World;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Doodads;

/// <summary>Fixed doodad spawn repositories for the doodad placement's tests.</summary>
internal static class FixedDoodadSpawns
{
    /// <summary>Every map without a fixed doodad spawn, as every procedural map is.</summary>
    public static IMapDoodadSpawnRepository None()
    {
        IMapDoodadSpawnRepository spawns = Substitute.For<IMapDoodadSpawnRepository>();
        spawns.FindByMapAsync(Arg.Any<MapTemplateId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MapDoodadSpawn>>([]));
        return spawns;
    }
}

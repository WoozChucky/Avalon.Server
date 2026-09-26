using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Instances;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// A real MapInstance over a one-chunk layout and a substitute navigator, for tests that drive a
/// kill through OnCreatureKilled. Map template 1, no owner, seed 0.
/// </summary>
internal static class TestMapInstances
{
    /// <param name="scripts">The script manager the instance builds ability scripts from; a substitute that finds none when omitted.</param>
    /// <param name="navigator">The instance's navigator; a bare substitute when omitted.</param>
    public static MapInstance Build(IWorld world, IScriptManager? scripts = null, IMapNavigator? navigator = null)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(scripts ?? Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(
            Seed: 0,
            Chunks: [entryChunk],
            EntryChunk: entryChunk,
            BossChunk: null,
            Portals: [],
            EntrySpawnWorldPos: Vector3.zero,
            CellSize: 30f,
            Config: null);

        return new MapInstance(
            NullLoggerFactory.Instance,
            serviceProvider,
            world,
            new MapTemplateId(1),
            ownerCharacterId: null,
            layout,
            navigator ?? Substitute.For<IMapNavigator>(),
            seed: 0);
    }
}

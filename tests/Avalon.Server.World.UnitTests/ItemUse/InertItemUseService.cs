using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Inventory;
using Avalon.World.Items;
using Avalon.World.Persistence;
using Avalon.World.Respawn;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.ItemUse;

/// <summary>
/// An ItemUseService for the hand-built containers a test WorldServer resolves its handlers from, so ItemUseHandler
/// can be built. Every collaborator is a substitute: no use in those tests reaches it.
/// </summary>
internal static class InertItemUseService
{
    public static ItemUseService Create()
    {
        IWorld world = Substitute.For<IWorld>();
        var tools = new ItemUseTools(world, Substitute.For<ICharacterEconomy>(),
            new TownReturn(NullLogger.Instance, world, Substitute.For<IRespawnTargetResolver>(),
                Substitute.For<IChunkLibrary>()),
            new MapTeleport(NullLogger<MapTeleport>.Instance, world, Substitute.For<IChunkLibrary>(),
                Substitute.For<ICharacterSaver>()),
            Substitute.For<ICreaturePlacementService>(), TimeProvider.System, NullLogger<ItemUseTools>.Instance);
        return new ItemUseService(tools, Substitute.For<IScriptManager>(), Substitute.For<IServiceProvider>(),
            NullLogger<ItemUseService>.Instance);
    }
}

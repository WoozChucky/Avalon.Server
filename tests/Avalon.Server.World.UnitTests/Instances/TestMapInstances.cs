using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Units;
using Avalon.World.Handlers;
using Avalon.World.Scripts.Abilities;
using Avalon.World.Instances;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Pvp;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>
/// A real MapInstance over a one-chunk layout and a substitute navigator, for tests that drive a
/// kill through the instance's kill handling. Map template 1, no owner, seed 0.
/// </summary>
internal static class TestMapInstances
{
    /// <summary>
    /// An instance the real cast handler can reach: the world's registry finds it, the script manager
    /// finds the three shape scripts, and any <paramref name="extraScripts" />, by name, and the
    /// navigator lets every ray through.
    /// </summary>
    public static MapInstance BuildCasting(out CastAbilityHandler handler, MapType mapType = MapType.Normal,
        params Type[] extraScripts)
    {
        var scripts = Substitute.For<IScriptManager>();
        foreach (Type script in new[] { typeof(CircleAbilityScript), typeof(ConeAbilityScript), typeof(ProjectileAbilityScript) }
                     .Concat(extraScripts))
        {
            scripts.GetAbilityScript(script.Name).Returns(script);
        }

        var navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(ci => ci.ArgAt<Vector3>(1));
        IWorld world = MapInstanceClients.NewWorld();
        MapInstance instance = Build(world, scripts, navigator, mapType: mapType);
        world.InstanceRegistry.GetInstanceById(instance.InstanceId).Returns(instance);
        handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());
        return instance;
    }

    /// <summary>
    /// Reports <paramref name="creature" /> killed by <paramref name="killer" />, exactly as the
    /// instance's combat service does once a hit brings the creature to 0 health (#546).
    /// </summary>
    public static void ReportKill(this MapInstance instance, ICreature creature, IUnit killer) =>
        ((ICombatOutcomes)instance).CreatureKilled(creature, killer);

    /// <param name="scripts">The script manager the instance builds ability scripts from; a substitute that finds none when omitted.</param>
    /// <param name="navigator">The instance's navigator; a bare substitute when omitted.</param>
    /// <param name="pvp">The PvP toggle the instance and its combat service use; the instance builds its own when omitted.</param>
    /// <param name="mapType">The instance's map type; Normal when omitted.</param>
    public static MapInstance Build(
        IWorld world, IScriptManager? scripts = null, IMapNavigator? navigator = null, PvpToggle? pvp = null,
        MapType mapType = MapType.Normal)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(scripts ?? Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        if (pvp is not null)
        {
            serviceProvider.GetService(typeof(PvpToggle)).Returns(pvp);
        }

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
            seed: 0,
            mapType: mapType);
    }
}

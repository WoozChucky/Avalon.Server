using Avalon.Combat;
using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.World;
using Avalon.World.Auras;
using Avalon.World.ChunkLayouts;
using Avalon.World.Combat;
using Avalon.World.Handlers;
using Avalon.World.Instances;
using Avalon.World.Loot;
using Avalon.World.Parties;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Maps;
using Avalon.World.Public.Units;
using Avalon.World.Pvp;
using Avalon.World.Quests;
using Avalon.World.Scripts;
using Avalon.World.Scripts.Abilities;
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
    /// <param name="world">The world the instance belongs to; <see cref="MapInstanceClients.NewWorld" /> when omitted.</param>
    /// <param name="random">Every combat roll (#506); the instance's own no-proc fallback when omitted.</param>
    /// <param name="time">The container's clock; the system clock when omitted.</param>
    /// <param name="auraScripts">Runs the auras' scripts; none runs when omitted.</param>
    public static MapInstance BuildCasting(out CastAbilityHandler handler, MapType mapType = MapType.Normal,
        IWorld? world = null, ICombatRandom? random = null, TimeProvider? time = null, AuraScripts? auraScripts = null,
        params Type[] extraScripts)
    {
        IScriptManager scripts = Substitute.For<IScriptManager>();
        foreach (Type script in new[] { typeof(CircleAbilityScript), typeof(ConeAbilityScript), typeof(ProjectileAbilityScript) }
                     .Concat(extraScripts))
        {
            scripts.GetAbilityScript(script.Name).Returns(script);
        }

        IMapNavigator navigator = Substitute.For<IMapNavigator>();
        navigator.RaycastWalkable(default, default).ReturnsForAnyArgs(ci => ci.ArgAt<Vector3>(1));
        world ??= MapInstanceClients.NewWorld();
        MapInstance instance = Build(world, scripts, navigator, mapType: mapType, time: time, random: random,
            auraScripts: auraScripts);
        world.InstanceRegistry.GetInstanceById(instance.InstanceId).Returns(instance);
        handler = new CastAbilityHandler(NullLogger<CastAbilityHandler>.Instance, world, new CombatConfig());
        return instance;
    }

    /// <summary>
    /// Reports <paramref name="creature" /> killed by <paramref name="killer" />, exactly as the
    /// instance's combat service does once a hit brings the creature to 0 health (#546).
    /// </summary>
    public static void ReportKill(this MapInstance instance, ICreature creature, IUnit? killer) =>
        ((ICombatOutcomes)instance).CreatureKilled(creature, killer);

    /// <param name="scripts">The script manager the instance builds ability scripts from; a substitute that finds none when omitted.</param>
    /// <param name="navigator">The instance's navigator; a bare substitute when omitted.</param>
    /// <param name="pvp">The PvP toggle the instance and its combat service use; the instance builds its own when omitted.</param>
    /// <param name="mapType">The instance's map type; Normal when omitted.</param>
    /// <param name="time">The container's clock; the system clock when omitted.</param>
    /// <param name="random">Every combat roll (#506); the instance's own no-proc fallback when omitted.</param>
    /// <param name="ownerPartyId">The party that owns the instance; none when omitted.</param>
    /// <param name="templateId">The instance's map template; map 1 when omitted.</param>
    /// <param name="parties">The party service kills are shared through; none when omitted, so every kill is solo.</param>
    /// <param name="quests">The quest service kills are credited through; none when omitted.</param>
    /// <param name="lootRoller">The loot roller a kill drops through; none when omitted, so no kill drops anything.</param>
    /// <param name="lootAllocator">Who a drop is reserved for; none when omitted, so no kill drops anything.</param>
    /// <param name="auraScripts">Runs the auras' scripts; none runs when omitted.</param>
    public static MapInstance Build(
        IWorld world, IScriptManager? scripts = null, IMapNavigator? navigator = null, PvpToggle? pvp = null,
        MapType mapType = MapType.Normal, TimeProvider? time = null, ICombatRandom? random = null,
        PartyId? ownerPartyId = null, MapTemplateId? templateId = null, PartyService? parties = null,
        QuestService? quests = null, ILootRoller? lootRoller = null, ILootAllocator? lootAllocator = null,
        AuraScripts? auraScripts = null)
    {
        IServiceProvider serviceProvider = Substitute.For<IServiceProvider>();
        if (random is not null)
        {
            serviceProvider.GetService(typeof(ICombatRandom)).Returns(random);
        }
        if (time is not null)
        {
            serviceProvider.GetService(typeof(TimeProvider)).Returns(time);
        }
        serviceProvider.GetService(typeof(IScriptManager)).Returns(scripts ?? Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        if (pvp is not null)
        {
            serviceProvider.GetService(typeof(PvpToggle)).Returns(pvp);
        }
        if (parties is not null)
        {
            serviceProvider.GetService(typeof(PartyService)).Returns(parties);
        }
        if (quests is not null)
        {
            serviceProvider.GetService(typeof(QuestService)).Returns(quests);
        }
        if (lootRoller is not null)
        {
            serviceProvider.GetService(typeof(ILootRoller)).Returns(lootRoller);
        }
        if (lootAllocator is not null)
        {
            serviceProvider.GetService(typeof(ILootAllocator)).Returns(lootAllocator);
        }
        if (auraScripts is not null)
        {
            serviceProvider.GetService(typeof(AuraScripts)).Returns(auraScripts);
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
            templateId ?? new MapTemplateId(1),
            ownerCharacterId: null,
            layout,
            navigator ?? Substitute.For<IMapNavigator>(),
            seed: 0,
            mapType: mapType,
            ownerPartyId: ownerPartyId);
    }
}

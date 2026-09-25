using Avalon.Common.Mathematics;
using Avalon.Common.ValueObjects;
using Avalon.Domain.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Entities;
using Avalon.World.Instances;
using Avalon.World.Persistence;
using Avalon.World.Public;
using Avalon.World.Public.Abilities;
using Avalon.World.Public.Combat;
using Avalon.World.Public.Maps;
using Avalon.World.Scripts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Instances;

/// <summary>The scheduler only saves anyone because the instance ticks it, once per character per tick.</summary>
public class MapInstanceSaveHookShould
{
    [Fact]
    public void Tick_the_save_scheduler_for_every_character()
    {
        var scheduler = Substitute.For<ICharacterSaveScheduler>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IScriptManager)).Returns(Substitute.For<IScriptManager>());
        serviceProvider.GetService(typeof(CombatConfig)).Returns(new CombatConfig());
        serviceProvider.GetService(typeof(ICharacterSaveScheduler)).Returns(scheduler);

        var world = Substitute.For<Avalon.World.IWorld>();
        world.Configuration.Returns(new GameConfiguration());

        var entryChunk = new PlacedChunk(new ChunkTemplateId(1), 0, 0, 0, Vector3.zero);
        var layout = new ChunkLayout(Seed: 0, Chunks: [entryChunk], EntryChunk: entryChunk, BossChunk: null,
            Portals: [], EntrySpawnWorldPos: Vector3.zero, CellSize: 30f, Config: null);

        var instance = new MapInstance(NullLoggerFactory.Instance, serviceProvider, world, new MapTemplateId(1),
            ownerCharacterId: null, layout, Substitute.For<IMapNavigator>(), seed: 0);
        instance.Dispose();   // detach the static entity events; this test raises none

        CharacterEntity character = New(7);
        character.Spells.Load(Array.Empty<IAbility>());   // the tick updates abilities; an unloaded list throws
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(character);
        connection.CryptoSession.Returns(new FakeAvalonCryptoSession());
        instance.AddCharacter(connection);

        TimeSpan delta = TimeSpan.FromSeconds(1d / 60d);
        instance.Update(delta);

        scheduler.Received(1).Tick(connection, character, delta);
    }
}

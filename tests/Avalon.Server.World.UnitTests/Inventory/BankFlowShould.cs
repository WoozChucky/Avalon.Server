using Avalon.Database.Auth.Repositories;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.World;
using Avalon.World.ChunkLayouts;
using Avalon.World.Configuration;
using Avalon.World.Handlers;
using Avalon.World.Inventory;
using Avalon.World.Maps;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Instances;
using Avalon.World.Scripts.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Avalon.Server.World.UnitTests.Inventory.TestCharacters;

namespace Avalon.Server.World.UnitTests.Inventory;

/// <summary>
/// The bank across an instance change (spec #463): entering a map or respawning at a town goes
/// through World.TransferPlayer, and the banker stays behind, so the bank must close there rather
/// than stay usable from the next map.
/// </summary>
public class BankFlowShould
{
    [Fact]
    public async Task Close_the_bank_when_the_character_changes_instance()
    {
        var w = new BankerWorld();
        w.Character.Container(InventoryType.Bank).Load([Item(2, Potion, count: 7)]);
        OpenBankThroughTheDialogue(w);
        Assert.True(BankAccess.TryUse(w.Connection, w.Character, w.World));
        Avalon.World.World world = await RealWorldAsync();

        world.TransferPlayer(w.Connection, Elsewhere());

        Assert.False(BankAccess.IsOpen(w.Connection, w.Character));
        Assert.Null(w.Character.OpenBankNpc);
        Assert.Null(w.Connection.CurrentDialogue);
        SDialogueEndPacket end = Assert.Single(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
        Assert.Equal(BankerWorld.BankerGuid.RawValue, end.SpeakerGuid);

        Assert.True(SlotRef.TryParse((uint)InventoryType.Bank, 2, out SlotRef banked));
        Assert.True(SlotRef.TryParse((uint)InventoryType.Bag, 0, out SlotRef bag));
        ItemRequestResult result = InventoryFor(w.Character)
            .TryMove(banked, bag, null, BankAccess.TryUse(w.Connection, w.Character, w.World));
        Assert.Equal(ItemRequestResult.BankClosed, result);
    }

    [Fact]
    public async Task Send_no_dialogue_end_on_a_transfer_with_no_conversation_open()
    {
        var w = new BankerWorld();
        Avalon.World.World world = await RealWorldAsync();

        world.TransferPlayer(w.Connection, Elsewhere());

        Assert.Empty(w.Read<SDialogueEndPacket>(NetworkPacketType.SMSG_DIALOGUE_END));
    }

    [Fact]
    public async Task Close_the_bank_when_the_character_leaves_the_world()
    {
        var w = new BankerWorld();
        OpenBankThroughTheDialogue(w);
        Avalon.World.World world = await RealWorldAsync();

        await world.DeSpawnPlayerAsync(w.Connection);

        Assert.Null(w.Character.OpenBankNpc);
    }

    private static void OpenBankThroughTheDialogue(BankerWorld w)
    {
        new InteractHandler(NullLogger<InteractHandler>.Instance, w.World).Execute(w.Connection,
            new CInteractPacket { TargetGuid = BankerWorld.BankerGuid.RawValue });
        new DialogueChooseHandler(NullLogger<DialogueChooseHandler>.Instance, w.World).Execute(w.Connection,
            new CDialogueChoosePacket
            {
                TargetGuid = BankerWorld.BankerGuid.RawValue, NodeId = BankerWorld.BankerRoot,
                OptionId = BankerWorld.OpenBankOption,
            });
        Assert.True(BankAccess.IsOpen(w.Connection, w.Character));
    }

    private static IMapInstance Elsewhere()
    {
        var instance = Substitute.For<IMapInstance>();
        instance.InstanceId.Returns(new Guid("46300000-0000-0000-0000-000000000999"));
        return instance;
    }

    /// <summary>A real World, loaded, whose registry holds no instance, so only the transfer itself is observed.</summary>
    private static async Task<Avalon.World.World> RealWorldAsync()
    {
        var worldRepository = Substitute.For<IWorldRepository>();
        worldRepository.FindByIdAsync(Arg.Any<Avalon.Domain.Auth.WorldId>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Avalon.Domain.Auth.World
            {
                Name = "test", Host = "127.0.0.1", Port = 0, MinVersion = "0.0.1", Version = "1.0.0",
            });

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IChunkLayoutInstanceFactory))
            .Returns(Substitute.For<IChunkLayoutInstanceFactory>());

        TestStaticDataRepositories r = TestStaticData.Repositories();
        var world = new Avalon.World.World(
            NullLoggerFactory.Instance,
            Options.Create(new GameConfiguration { WorldId = new Avalon.Domain.Auth.WorldId(1) }),
            serviceProvider,
            worldRepository,
            Substitute.For<IAvalonMapManager>(),
            Substitute.For<IServiceScopeFactory>(),
            r.CreateInfos, r.ClassStats, r.Items, r.Abilities, r.Levels, r.Creatures, r.BaseStats, r.Rarities,
            r.Texts,
            Substitute.For<IScriptHotReloader>(),
            Substitute.For<IChunkLibrary>(),
            r.Dialogue, r.Loot);

        await world.LoadAsync(CancellationToken.None);
        return world;
    }
}

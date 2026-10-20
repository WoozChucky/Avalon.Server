using Avalon.Server.World.UnitTests.Auras;
using Avalon.Common;
using Avalon.Network.Packets.Combat;
using Avalon.Server.World.UnitTests.Inventory;
using Avalon.World.Auras;
using Avalon.World.Entities;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Handlers;

public class TargetUnitHandlerShould
{
    [Theory]
    [InlineData(ObjectType.Character, 0UL, true)]
    [InlineData(ObjectType.Creature, 0UL, false)]
    [InlineData(ObjectType.Character, 1UL << 40, false)]
    public void Clear_only_a_protected_characters_selection_without_aliasing_other_guids(
        ObjectType type, ulong extraBits, bool protectedTarget)
    {
        Avalon.World.IWorld world = Substitute.For<Avalon.World.IWorld>();
        IMapInstance instance = Substitute.For<IMapInstance>();
        CharacterEntity viewer = TestCharacters.New(1);
        CharacterEntity target = TestCharacters.New(2);
        target.Health = target.CurrentHealth = viewer.Health = viewer.CurrentHealth = 100;
        world.InstanceRegistry.GetInstanceById(viewer.InstanceId).Returns(instance);
        instance.Characters.Returns(new Dictionary<ObjectGuid, ICharacter> { [target.Guid] = target });
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.Character.Returns(viewer);
        connection.CurrentTargetGuid = target.Guid.RawValue;
        ArrivalTestData.Apply(target);
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton(world)
            .AddLogging()
            .BuildServiceProvider();
        TargetUnitHandler handler = ActivatorUtilities.CreateInstance<TargetUnitHandler>(services);
        ulong raw = new ObjectGuid(type, target.Guid.Id).RawValue | extraBits;
        var packet = new CTargetUnitPacket { TargetGuid = raw };

        handler.Execute(connection, packet);
        Assert.Equal(protectedTarget ? (ulong?)null : raw, connection.CurrentTargetGuid);

        ArrivalProtection.End(target);
        handler.Execute(connection, packet);
        Assert.Equal(raw, connection.CurrentTargetGuid);
    }
}

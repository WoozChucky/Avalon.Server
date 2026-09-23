using Avalon.Common;
using Avalon.Common.ValueObjects;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Instances;
using NSubstitute;

namespace Avalon.Server.World.UnitTests.Characters;

/// <summary>
/// A substituted connection whose Character and pending spawn behave the way WorldConnection's do.
/// Both need saying: a substitute auto-fills an interface-typed property rather than starting it
/// null, so an un-taught Character reads as "already selected", and TakePendingSpawn has to clear
/// what it returns or the tick would spawn a released character again every tick.
/// <see cref="WorldConnectionPendingSpawnShould" /> holds the real implementation to the same rules.
/// Tests about the window BEFORE a pending spawn exists drive the real connection instead -- see
/// <see cref="CharacterSelectChainShould" />.
/// </summary>
internal static class PendingSpawnConnection
{
    /// <summary>
    ///     A stand-in character with a real ObjectGuid. A bare substitute returns null for it --
    ///     ObjectGuid is a class -- and the paths that name a character in a log read through it.
    /// </summary>
    public static ICharacter Character(string name = "Tester", uint id = 7)
    {
        var character = Substitute.For<ICharacter>();
        character.Name.Returns(name);
        character.Guid.Returns(new ObjectGuid(ObjectType.Character, id));
        return character;
    }

    public static IWorldConnection Create(PendingSpawn? pending = null)
    {
        var connection = Substitute.For<IWorldConnection>();
        connection.AccountId.Returns(new AccountId(42L));
        // A bare substitute reports false, and the tick skips a connection that is not up -- so
        // without this the barrier sweep silently does nothing in every test that uses one.
        connection.IsConnected.Returns(true);

        ICharacter? character = null;
        connection.Character.Returns(_ => character);
        connection.When(c => c.Character = Arg.Any<ICharacter>())
#pragma warning disable NS3002 // the analyzer reads the property getter, which takes no argument; the setter does
            .Do(ci => character = ci.Arg<ICharacter>());
#pragma warning restore NS3002

        PendingSpawn? held = pending;
        connection.PendingSpawn.Returns(_ => held);
        connection.TakePendingSpawn().Returns(_ =>
        {
            PendingSpawn? taken = held;
            held = null;
            return taken;
        });
        connection
            .When(c => c.SetPendingSpawn(Arg.Any<ICharacter>(), Arg.Any<IMapInstance>(), Arg.Any<long>()))
            .Do(ci =>
            {
                held = new PendingSpawn(ci.Arg<ICharacter>(), ci.Arg<IMapInstance>(), ci.Arg<long>());
                // Mirrors the real connection: the pending spawn supersedes the in-flight select.
                connection.SelectInProgress.Returns(false);
            });

        return connection;
    }
}

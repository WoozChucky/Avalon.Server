using Avalon.Common.Cryptography;
using Avalon.Common.ValueObjects;
using Avalon.Database.Character;
using Avalon.Database.Character.Repositories;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Configuration;
using Avalon.World.Handlers;
using Avalon.World.Public;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProtoBuf;
using Xunit;
using CharacterRow = Avalon.Domain.Characters.Character;

namespace Avalon.Server.World.UnitTests.Handlers;

/// <summary>
/// The character list a client is sent shows the account's characters oldest first (#727), over
/// the real repository and a real database, whatever order the rows were written in.
/// </summary>
public class CharacterListHandlerShould : IDisposable
{
    private static readonly AccountId Owner = new(1);
    private static readonly DateTime Day = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SqliteDatabase<CharacterDbContext> _characters = SqliteDatabase.Characters();

    [Fact]
    public async Task List_the_accounts_characters_oldest_first()
    {
        await using (CharacterDbContext write = _characters.CreateDbContext())
        {
            write.Characters.AddRange(
                Row(1, "Newest", Day.AddDays(2)),
                Row(2, "Oldest", Day),
                Row(4, "TieLater", Day.AddDays(1)),
                Row(3, "TieEarlier", Day.AddDays(1)));
            await write.SaveChangesAsync();
        }

        IWorld world = Substitute.For<IWorld>();
        world.Configuration.Returns(new GameConfiguration { MaxCharactersPerAccount = 10 });

        Task<List<CharacterRow>>? query = null;
        Action<List<CharacterRow>>? callback = null;
        IWorldConnection connection = Substitute.For<IWorldConnection>();
        connection.AccountId.Returns(Owner);
        connection.Character.Returns((Avalon.World.Public.Characters.ICharacter?)null!);
        connection.CryptoSession.Returns(new EchoCryptoSession());
        connection.EnqueueContinuation(
            Arg.Do<Task<List<CharacterRow>>>(t => query = t),
            Arg.Do<Action<List<CharacterRow>>>(c => callback = c));

        new CharacterListHandler(NullLogger<CharacterListHandler>.Instance, new CharacterRepository(_characters), world)
            .Execute(connection, new CCharacterListPacket());

        Assert.NotNull(query);
        callback!(await query!);

        NetworkPacket sent = (NetworkPacket)connection.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IWorldConnection.Send))
            .GetArguments()[0]!;
        SCharacterListPacket list = Serializer.Deserialize<SCharacterListPacket>(new MemoryStream(sent.Payload));

        Assert.Equal(["Oldest", "TieEarlier", "TieLater", "Newest"], list.Characters.Select(c => c.Name));
    }

    private static CharacterRow Row(uint id, string name, DateTime created) => new()
    {
        Id = new CharacterId(id),
        AccountId = Owner,
        Name = name,
        Class = CharacterClass.Warrior,
        CreationDate = created,
    };

    public void Dispose() => _characters.Dispose();

    private sealed class EchoCryptoSession : IAvalonCryptoSession
    {
        public void Initialize(byte[] otherEndPublicKeyBytes) { }
        public byte[] GetPublicKey() => [];
        public byte[] GetOtherEndPublicKey() => [];
        public byte[] Encrypt(ReadOnlySpan<byte> data) => data.ToArray();
        public int Decrypt(ReadOnlySpan<byte> data, byte[] output) => 0;
        public byte[] GenerateHandshakeData() => [];
    }
}

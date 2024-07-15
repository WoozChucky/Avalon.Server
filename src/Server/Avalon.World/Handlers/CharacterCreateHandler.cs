
using Avalon.Database.Character.Repositories;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Character;
using Avalon.World;
using Avalon.World.Database.Repositories;
using Microsoft.Extensions.Logging;

[PacketHandler(NetworkPacketType.CMSG_CHARACTER_CREATE)]
public class CharacterCreateHandler : WorldPacketHandler<CCharacterCreatePacket>
{
    private readonly ILogger<CharacterCreateHandler> _logger;
    private readonly ICharacterRepository _characterRepository;
    private readonly ICharacterCreateInfoRepository _createInfoRepository;
    private readonly IClassLevelStatRepository _classLevelStatRepository;
    private readonly ICharacterStatsRepository _characterStatsRepository;
    private readonly ICharacterSpellRepository _characterSpellRepository;
    private readonly ICharacterInventoryRepository _characterInventoryRepository;
    private readonly IItemTemplateRepository _itemTemplateRepository;
    private readonly IItemInstanceRepository _itemInstanceRepository;
    private readonly IWorld _world;

    public CharacterCreateHandler(
        ILogger<CharacterCreateHandler> logger,
        ICharacterRepository characterRepository,
        ICharacterCreateInfoRepository createInfoRepository,
        IClassLevelStatRepository classLevelStatRepository,
        ICharacterStatsRepository characterStatsRepository,
        ICharacterSpellRepository characterSpellRepository,
        ICharacterInventoryRepository characterInventoryRepository,
        IItemTemplateRepository itemTemplateRepository,
        IItemInstanceRepository itemInstanceRepository,
        IWorld world)
    {
        _logger = logger;
        _characterRepository = characterRepository;
        _createInfoRepository = createInfoRepository;
        _classLevelStatRepository = classLevelStatRepository;
        _characterStatsRepository = characterStatsRepository;
        _characterSpellRepository = characterSpellRepository;
        _characterInventoryRepository = characterInventoryRepository;
        _itemTemplateRepository = itemTemplateRepository;
        _itemInstanceRepository = itemInstanceRepository;
        _world = world;
    }

    public override void Execute(WorldConnection connection, CCharacterCreatePacket packet)
    {
        if (connection.AccountId == null)
        {
            _logger.LogWarning("Connection tried to create a character without being authenticated");
            connection.Close();
            return;
        }

        if (connection.Character != null)
        {
            _logger.LogWarning("Connection tried to create a character while already having a character selected");
            connection.Close();
            return;
        }

        connection.AddQueryCallback(_characterRepository.FindByAccountAsync(connection.AccountId), characters =>
        {
            OnCharactersReceived(connection, characters, packet);
        });
    }

    private void OnCharactersReceived(WorldConnection connection, IList<Character> characters, CCharacterCreatePacket packet)
    {
        var currentCharacterCount = characters.Count;
        
        if (currentCharacterCount == _world.Configuration.MaxCharactersPerAccount || currentCharacterCount + 1 > _world.Configuration.MaxCharactersPerAccount)
        {
            _logger.LogDebug("Account {AccountId} already has {CharacterCount} characters", connection.AccountId, currentCharacterCount);
            connection.Send(SCharacterCreatedPacket.Create(SCharacterCreateResult.MaxCharactersReached, connection.CryptoSession.Encrypt));
            return;
        }
        
        connection.AddQueryCallback(_characterRepository.FindByNameAsync(packet.Name), character => {

        });
    }
}
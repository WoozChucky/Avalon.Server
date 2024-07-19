using Avalon.Common.Mathematics;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public class CharacterEntity : ICharacter
{
    public static event CharacterDisconnectedDelegate? CharacterDisconnected;
    public IWorldConnection Connection => _connection;
    public IGameState GameState { get; }

    public ICharacterInventory this[InventoryType type] => type switch
    {
        InventoryType.Equipment => _equipment,
        InventoryType.Bag => _bag,
        InventoryType.Bank => _bank,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public ICharacterSpells Spells => _spells;

    private readonly ILogger<CharacterEntity> _logger;
    private readonly IWorldConnection _connection;
    private readonly ICharacterInventory _equipment;
    private readonly ICharacterInventory _bag;
    private readonly ICharacterInventory _bank;
    private readonly ICharacterSpells _spells;

    public CharacterEntity()
    {
        _logger = null!;
        _connection = null!;
        _equipment = null!;
        _bag = null!;
        _bank = null!;
        _spells = null!;
    }
    
    public CharacterEntity(ILoggerFactory loggerFactory, IWorldConnection connection)
    {
        _logger = loggerFactory.CreateLogger<CharacterEntity>();
        _connection = connection;
        _equipment = new CharacterInventoryContainer(loggerFactory, InventoryType.Equipment);
        _bag = new CharacterInventoryContainer(loggerFactory, InventoryType.Bag);
        _bank = new CharacterInventoryContainer(loggerFactory, InventoryType.Bank);
        _spells = new CharacterSpellContainer(loggerFactory);
        GameState = new GameState();
    }

    public ulong Id
    {
        get => Data?.Id ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Id = value;
            }
        }
    }

    public uint Health
    {
        get => (uint) (Data?.Health ?? 0);
        set
        {
            if (Data != null)
            {
                Data.Health = (int) value;
            }
        }
    }
    
    public uint CurrentHealth { get; set; }

    public uint Power
    {
        get => (uint) (Data?.Power1 ?? 0);
        set
        {
            if (Data != null)
            {
                Data.Power1 = (int) value;
            }
        }
    }
    public uint CurrentPower { get; set; }
    public MoveState MoveState { get; set; } = MoveState.Idle;

    public Vector3 Position
    {
        get => new(Data?.X ?? 0, Data?.Y ?? 0, Data?.Z ?? 0);
        set
        {
            if (Data == null) return;
            Data.X = value.x;
            Data.Y = value.y;
            Data.Z = value.z;
        }
    }
    
    public Vector3 Velocity { get; set; }

    public Vector3 Orientation
    {
        get => new(0, Data?.Rotation ?? 0, 0);
        set
        {
            if (Data != null)
            {
                Data.Rotation = value.y;
            }
        }
    }
    
    public Character? Data { get; init; }

    public string Name
    {
        get => Data?.Name ?? string.Empty;
        set
        {
            if (Data != null)
            {
                Data.Name = value;
            }
        }
    }
    
    public ushort Map
    {
        get => Data?.Map ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Map = value;
            }
        }
    }
    
    public ushort Level
    {
        get => Data?.Level ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Level = value;
            }
        }
    }

    public void OnHit(ICharacter attacker, uint damage)
    {
        throw new NotImplementedException();
    }

    public void OnHit(ICreature attacker, uint damage)
    {
        _logger.LogInformation("{Name} has been hit by {Attacker} for {Damage} damage", Name, attacker.Name, damage);
        CurrentHealth -= damage;
        if (CurrentHealth <= 0)
        {
            _logger.LogInformation("{Name} has died", Name);
            CurrentHealth = Health; // reset health while developing
        }
        _connection.Send(SCharacterDamagePacket.Create(attacker.Id, Id, CurrentHealth, damage, null, _connection.CryptoSession.Encrypt));
    }

    public void OnDisconnected()
    {
        CharacterDisconnected?.Invoke(this);
    }

    public uint ChunkId { get; set; }

    public DateTime EnteredWorld { get; set; }
}

public class CharacterInventoryContainer : ICharacterInventory
{
    private readonly InventoryType Type;
    
    private readonly ILogger<CharacterInventoryContainer> _logger;
    
    private ushort MaxSlots => Type switch
    {
        InventoryType.Equipment => 14,
        InventoryType.Bag => 30,
        InventoryType.Bank => 30,
        _ => throw new ArgumentOutOfRangeException()
    };
    
    public CharacterInventoryContainer(ILoggerFactory loggerFactory, InventoryType type)
    {
        _logger = loggerFactory.CreateLogger<CharacterInventoryContainer>();
        Type = type;
    }
    
    public void Load(IReadOnlyCollection<object> items)
    {
        var castedItems = items.Cast<CharacterInventory>().ToList();
        _logger.LogInformation("Loading {Count} items into {Type} inventory", castedItems.Count, Type);
    }
}

public class CharacterSpellContainer : ICharacterSpells
{
    private readonly ILogger<CharacterSpellContainer> _logger;
    
    public CharacterSpellContainer(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<CharacterSpellContainer>();
    }
    
    public void Load(IReadOnlyCollection<object> spells)
    {
        var castedSpells = spells.Cast<CharacterSpell>().ToList();
        _logger.LogInformation("Loading {Count} spells into character", castedSpells.Count);
    }
}

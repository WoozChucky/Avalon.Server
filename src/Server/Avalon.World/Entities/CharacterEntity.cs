using Avalon.Common;
using Avalon.Common.Mathematics;
using Avalon.Domain.Characters;
using Avalon.Network.Packets.Combat;
using Avalon.Network.Packets.State;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;
using Avalon.World.Public.Spells;
using Avalon.World.Spells;
using Microsoft.Extensions.Logging;

namespace Avalon.World.Entities;

public class CharacterEntity : ICharacter
{
    public static event UnitFinishedCastAnimationDelegate? OnUnitFinishedCastAnimation;
    public static event UnitAttackAnimationDelegate? OnUnitAttackAnimation;
    public static event CharacterDisconnectedDelegate? CharacterDisconnected;
    public static event UnitInterruptedCastAnimationDelegate? OnUnitInterruptedCastAnimation;
    public static event UnitDamagedDelegate? OnUnitDamaged;
    
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
    
    public CharacterEntity(ILoggerFactory loggerFactory, IWorldConnection connection, Character character)
    {
        _logger = loggerFactory.CreateLogger<CharacterEntity>();
        _connection = connection;
        Data = character;
        _equipment = new CharacterInventoryContainer(loggerFactory, InventoryType.Equipment);
        _bag = new CharacterInventoryContainer(loggerFactory, InventoryType.Bag);
        _bank = new CharacterInventoryContainer(loggerFactory, InventoryType.Bank);
        _spells = new CharacterSpellContainer(loggerFactory);
        GameState = new CharacterGameState();
        Guid = new ObjectGuid(ObjectType.Character, character.Id);
    }

    public ObjectGuid Guid { get; set; }

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
    
    public PowerType PowerType { get; set; }
    
    public uint? Power
    {
        get => (uint) (Data?.Power1 ?? 0);
        set
        {
            if (Data != null)
            {
                Data.Power1 = (int) value!;
            }
        }
    }
    
    public uint? CurrentPower { get; set; }
    public MoveState MoveState { get; set; } = MoveState.Idle;
    
    public void OnHit(IUnit attacker, uint damage)
    {
        _logger.LogInformation("{Name} has been hit by unit {Attacker} for {Damage} damage", Name, attacker.Guid, damage);
        CurrentHealth -= damage;
        if (CurrentHealth <= 0)
        {
            _logger.LogInformation("{Name} has died", Name);
            CurrentHealth = Health; // reset health while developing
        }
        // Send to self
        _connection.Send(SCharacterDamagePacket.Create(attacker.Guid.RawValue, Guid.RawValue, CurrentHealth, damage, null, _connection.CryptoSession.Encrypt));
        // Send to chunk
        OnUnitDamaged?.Invoke(this, attacker, damage);
    }

    public void SendAttackAnimation(ISpell? spell)
    {
        OnUnitAttackAnimation?.Invoke(this, spell);
    }

    public void SendFinishCastAnimation(ISpell spell)
    {
        OnUnitFinishedCastAnimation?.Invoke(this, spell);
    }

    public void SendInterruptedCastAnimation(ISpell spell)
    {
        OnUnitInterruptedCastAnimation?.Invoke(this, spell);
    }

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

    public ulong Experience 
    {
        get => Data?.Experience ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Experience = value;
            }
        }
    }

    public ulong RequiredExperience { get; set; }

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

    private bool Running
    {
        get => Data?.Running ?? false;
        set
        {
            if (Data != null)
            {
                Data.Running = value;
            }
        }
    }
    
    private float MovementSpeed { get; set; }

    public void OnDisconnected()
    {
        CharacterDisconnected?.Invoke(this);
    }

    public float GetMovementSpeed()
    {
        return MovementSpeed;
    }

    public void SetRunning(bool running)
    {
        Running = running;
        CalculateMovementSpeed();
    }

    public void Update(TimeSpan deltaTime)
    {
        Spells.Update(deltaTime);
    }

    private void CalculateMovementSpeed()
    {
        const float defaultRunSpeed = 1.0f;
        const float defaultWalkSpeed = 0.4f;
        
        // In the future, speed modifiers will be applied here
        
        MovementSpeed = Running ? defaultRunSpeed : defaultWalkSpeed;
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

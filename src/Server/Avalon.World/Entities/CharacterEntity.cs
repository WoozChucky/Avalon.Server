using Avalon.Common.Mathematics;
using Avalon.Domain.Characters;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;

namespace Avalon.World.Entities;

public class CharacterEntity : ICharacter
{
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

    public int Health
    {
        get => Data?.Health ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Health = value;
            }
        }
    }
    
    public int CurrentHealth { get; set; }
    
    public int Mana
    {
        get => Data?.Power1 ?? 0;
        set
        {
            if (Data != null)
            {
                Data.Power1 = value;
            }
        }
    }
    
    public int CurrentMana { get; set; }

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

    public void OnHit(ICharacter attacker, int damage)
    {
        throw new NotImplementedException();
    }

    public void OnHit(ICreature attacker, int damage)
    {
        throw new NotImplementedException();
    }

    public ICharacterInventory Inventory { get; init; }
    public uint ChunkId { get; set; }

    public DateTime EnteredWorld { get; set; }
}


public class CharacterInventory : ICharacterInventory
{
    
    public Task LoadAsync()
    {
        return Task.CompletedTask;
    }
}

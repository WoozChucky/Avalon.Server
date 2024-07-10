using Avalon.Common.Mathematics;
using Avalon.Domain.Characters;
using Avalon.World.Public.Characters;

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
    
    public Vector3 Position { get; set; }
    public Vector3 Orientation { get; set; }
    public Vector3 Velocity { get; set; }
    
    public Character? Data { get; set; }

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
    
    // Non database properties
    public bool IsChatting { get; set; }
    public float ElapsedGameTime { get; set; }
    public DateTime EnteredWorld { get; set; }
}

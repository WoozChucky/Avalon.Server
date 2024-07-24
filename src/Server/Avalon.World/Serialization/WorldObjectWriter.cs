using Avalon.Common.Mathematics;
using Avalon.World.Public;
using Avalon.World.Public.Characters;
using Avalon.World.Public.Creatures;
using Avalon.World.Public.Enums;

namespace Avalon.World.Serialization;

public class WorldObjectWriter(byte[] buffer) : BinaryWriter(new MemoryStream(buffer))
{
    public void Write(IWorldObject worldObject)
    {
        Write(worldObject.Position);
        Write(worldObject.Velocity);
        Write(worldObject.Orientation.y);
    }
    
    public void Write(IUnit unit)
    {
        Write(unit as IWorldObject);
        Write((byte)unit.MoveState);
        Write(unit.Health);
        Write(unit.CurrentHealth);
        Write((byte)unit.PowerType);
        if (unit.PowerType != PowerType.None)
        {
            Write(unit.Power!.Value);
            Write(unit.CurrentPower!.Value);
        }
        Write(unit.Level);
    }

    public void Write(ICharacter character)
    {
        Write(character, GameEntityFields.All);
        Write(character.Name);
    }

    public void Write(ICreature creature)
    {
        Write(creature, GameEntityFields.All);
        Write(creature.Metadata.Id);
        Write(creature.Name);
    }

    private void Write(Vector3 vector)
    {
        Write(vector.x);
        Write(vector.y);
        Write(vector.z);
    }
    
    public void Reset()
    {
        BaseStream.Position = 0;
        Seek(0, SeekOrigin.Begin);
    }

    public void Write(IWorldObject worldObject, GameEntityFields fields)
    {
        Write((int)fields);
        
        if (fields.HasFlag(GameEntityFields.Position))
        {
            Write(worldObject.Position);
        }
        if (fields.HasFlag(GameEntityFields.Velocity))
        {
            Write(worldObject.Velocity);
        }
        if (fields.HasFlag(GameEntityFields.Orientation))
        {
            Write(worldObject.Orientation.y);
        }
    }

    public void Write(IUnit worldObject, GameEntityFields fields)
    {
        Write((int)fields);
        // Positioning
        {
            if (fields.HasFlag(GameEntityFields.Position))
            {
                Write(worldObject.Position);
            }
            if (fields.HasFlag(GameEntityFields.Velocity))
            {
                Write(worldObject.Velocity);
            }
            if (fields.HasFlag(GameEntityFields.Orientation))
            {
                Write(worldObject.Orientation.y);
            }
            
            if (fields.HasFlag(GameEntityFields.MoveState))
            {
                Write((byte)worldObject.MoveState);
            }
        }
        
        // Stats
        {
            if (fields.HasFlag(GameEntityFields.Health))
            {
                Write(worldObject.Health);
            }
            if (fields.HasFlag(GameEntityFields.CurrentHealth))
            {
                Write(worldObject.CurrentHealth);
            }

            if (fields.HasFlag(GameEntityFields.PowerType))
            {
                Write((byte)worldObject.PowerType);
                if (worldObject.PowerType != PowerType.None)
                {
                    if (fields.HasFlag(GameEntityFields.Power))
                    {
                        Write(worldObject.Power!.Value);
                    }
                    if (fields.HasFlag(GameEntityFields.CurrentPower))
                    {
                        Write(worldObject.CurrentPower!.Value);
                    }
                }
            }
        }
        
        if (fields.HasFlag(GameEntityFields.Level))
        {
            Write(worldObject.Level);
        }
        
    }
}

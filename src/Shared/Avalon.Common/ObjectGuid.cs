namespace Avalon.Common;

public enum ObjectType
{
    None,
    Character,
    Creature,
    Spell,
    SpellProjectile
}

public class ObjectGuid
{
    private const int TypeShift = 56;
    private const ulong TypeMask = 0xFF00000000000000;
    private const ulong IdMask = 0x000000FFFFFFFFFF;

    private ulong _guid;

    public ObjectGuid()
    {
        _guid = 0;
    }

    public ObjectGuid(ulong raw)
    {
        _guid = raw;
    }

    public ObjectGuid(ObjectType type, uint id)
    {
        _guid = ((ulong)type << TypeShift) | (id & IdMask);
    }

    public void Set(ObjectType type, uint id)
    {
        _guid = ((ulong)type << TypeShift) | (id & IdMask);
    }
    
    public ulong RawValue => _guid;

    public ObjectType Type => (ObjectType)((_guid & TypeMask) >> TypeShift);
    
    public uint Id => (uint)(_guid & IdMask);

    public bool IsEmpty => _guid == 0;

    public override bool Equals(object? obj)
    {
        if (obj is ObjectGuid guid)
        {
            return _guid == guid._guid;
        }
        return false;
    }

    public override int GetHashCode()
    {
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        return _guid.GetHashCode();
    }

    public static bool operator ==(ObjectGuid? a, ObjectGuid? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if ((object)a == null || (object)b == null)
        {
            return false;
        }
        return a._guid == b._guid;
    }

    public static bool operator !=(ObjectGuid? a, ObjectGuid? b)
    {
        return !(a == b);
    }
    
    public override string ToString()
    {
        return $"Type: {Type}, Id: {Id}";
    }
}

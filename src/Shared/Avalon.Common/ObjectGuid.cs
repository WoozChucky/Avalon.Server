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
    private const int TYPE_SHIFT = 56;
    private const ulong TYPE_MASK = 0xFF00000000000000;
    private const ulong ID_MASK = 0x000000FFFFFFFFFF;

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
        _guid = ((ulong)type << TYPE_SHIFT) | (id & ID_MASK);
    }

    public void Set(ObjectType type, uint id)
    {
        _guid = ((ulong)type << TYPE_SHIFT) | (id & ID_MASK);
    }

    public ulong RawValue => _guid;

    public ObjectType Type => (ObjectType)((_guid & TYPE_MASK) >> TYPE_SHIFT);

    public uint Id => (uint)(_guid & ID_MASK);

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

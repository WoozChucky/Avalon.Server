namespace Avalon.Common;

/// <summary>
///     Represents the type of an object.
/// </summary>
public enum ObjectType
{
    None,
    Character,
    Creature,
    Spell,
    SpellProjectile
}

/// <summary>
///     Represents a globally unique identifier for an object.
/// </summary>
public class ObjectGuid
{
    private const int TypeShift = 56;
    private const ulong TypeMask = 0xFF00000000000000;
    private const ulong IdMask = 0x000000FFFFFFFFFF;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ObjectGuid" /> class with a default value.
    /// </summary>
    public ObjectGuid() => RawValue = 0;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ObjectGuid" /> class with a specified raw value.
    /// </summary>
    /// <param name="raw">The raw value of the GUID.</param>
    public ObjectGuid(ulong raw) => RawValue = raw;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ObjectGuid" /> class with a specified type and ID.
    /// </summary>
    /// <param name="type">The type of the object.</param>
    /// <param name="id">The ID of the object.</param>
    public ObjectGuid(ObjectType type, uint id) => RawValue = ((ulong)type << TypeShift) | (id & IdMask);

    /// <summary>
    ///     Gets the raw value of the GUID.
    /// </summary>
    public ulong RawValue { get; private set; }

    /// <summary>
    ///     Gets the type of the object.
    /// </summary>
    public ObjectType Type => (ObjectType)((RawValue & TypeMask) >> TypeShift);

    /// <summary>
    ///     Gets the ID of the object.
    /// </summary>
    public uint Id => (uint)(RawValue & IdMask);

    /// <summary>
    ///     Gets a value indicating whether the GUID is empty.
    /// </summary>
    public bool IsEmpty => RawValue == 0;

    /// <summary>
    ///     Sets the type and ID of the object.
    /// </summary>
    /// <param name="type">The type of the object.</param>
    /// <param name="id">The ID of the object.</param>
    public void Set(ObjectType type, uint id) => RawValue = ((ulong)type << TypeShift) | (id & IdMask);

    /// <summary>
    ///     Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is ObjectGuid guid)
        {
            return RawValue == guid.RawValue;
        }

        return false;
    }

    /// <summary>
    ///     Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode() =>
        // ReSharper disable once NonReadonlyMemberInGetHashCode
        RawValue.GetHashCode();

    /// <summary>
    ///     Determines whether two specified GUIDs are equal.
    /// </summary>
    /// <param name="a">The first GUID to compare.</param>
    /// <param name="b">The second GUID to compare.</param>
    /// <returns>true if the two GUIDs are equal; otherwise, false.</returns>
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

        return a.RawValue == b.RawValue;
    }

    /// <summary>
    ///     Determines whether two specified GUIDs are not equal.
    /// </summary>
    /// <param name="a">The first GUID to compare.</param>
    /// <param name="b">The second GUID to compare.</param>
    /// <returns>true if the two GUIDs are not equal; otherwise, false.</returns>
    public static bool operator !=(ObjectGuid? a, ObjectGuid? b) => !(a == b);

    /// <summary>
    ///     Returns a string that represents the current object.
    /// </summary>
    /// <returns>A string that represents the current object.</returns>
    public override string ToString() => $"Type: {Type}, Id: {Id}";
}

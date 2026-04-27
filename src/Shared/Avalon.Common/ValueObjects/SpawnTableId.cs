namespace Avalon.Common.ValueObjects;

public class SpawnTableId : ValueObject<ushort>, IHideObjectMembers
{
    public SpawnTableId(ushort value) : base(value) { }

    public static implicit operator SpawnTableId(ushort value)
    {
        return new SpawnTableId(value);
    }
}

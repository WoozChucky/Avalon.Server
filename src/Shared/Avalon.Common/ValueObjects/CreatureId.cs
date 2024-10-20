namespace Avalon.Common.ValueObjects;

public class CreatureId : ValueObject<ulong>, IHideObjectMembers
{
    public CreatureId(ulong value) : base(value)
    {
    }

    public static implicit operator ulong(CreatureId creatureId) => creatureId.Value;
    public static implicit operator CreatureId(ulong value) => new CreatureId(value);
}

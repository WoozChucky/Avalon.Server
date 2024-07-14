namespace Avalon.Common.ValueObjects;

public class ItemInstanceId : ValueObject<Guid>
{
    public ItemInstanceId(Guid value) : base(value)
    {
    }
    
    public static implicit operator ItemInstanceId(Guid value)
    {
        return new ItemInstanceId(value);
    }
}

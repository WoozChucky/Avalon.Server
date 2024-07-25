namespace Avalon.Common;

public abstract class ValueObject<TValue> : IEquatable<ValueObject<TValue>>
    where TValue : IEquatable<TValue>
{
    public TValue Value { get; }
    
    protected ValueObject(TValue value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        Value = value;
    }
    
    public override bool Equals(object? obj)
    {
        return Equals(obj as ValueObject<TValue>);
    }

    public bool Equals(ValueObject<TValue>? other)
    {
        return other! != null! && EqualityComparer<TValue>.Default.Equals(Value, other.Value);
    }

    public override int GetHashCode()
    {
        return Value.GetHashCode();
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    // Implement the equality operators
    public static bool operator ==(ValueObject<TValue>? left, ValueObject<TValue>? right)
    {
        if (left is null && right is null)
            return true;
        return EqualityComparer<ValueObject<TValue>>.Default.Equals(left!, right!);
    }

    public static bool operator !=(ValueObject<TValue> left, ValueObject<TValue> right)
    {
        return !(left == right);
    }

    // Allow implicit conversion to the underlying value
    public static implicit operator TValue(ValueObject<TValue> valueObject)
    {
        return valueObject.Value;
    }
}

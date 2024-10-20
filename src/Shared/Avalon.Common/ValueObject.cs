namespace Avalon.Common;

/// <summary>
///     Represents an abstract base class for value objects.
/// </summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
public abstract class ValueObject<TValue> : IEquatable<ValueObject<TValue>>
    where TValue : IEquatable<TValue>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ValueObject{TValue}" /> class.
    /// </summary>
    /// <param name="value">The value of the value object.</param>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    protected ValueObject(TValue value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        Value = value;
    }

    /// <summary>
    ///     Gets the value of the value object.
    /// </summary>
    public TValue Value { get; }

    /// <summary>
    ///     Determines whether the specified <see cref="ValueObject{TValue}" /> is equal to the current
    ///     <see cref="ValueObject{TValue}" />.
    /// </summary>
    /// <param name="other">The value object to compare with the current value object.</param>
    /// <returns>true if the specified value object is equal to the current value object; otherwise, false.</returns>
    public bool Equals(ValueObject<TValue>? other) =>
        other! != null! && EqualityComparer<TValue>.Default.Equals(Value, other.Value);

    /// <summary>
    ///     Determines whether the specified object is equal to the current value object.
    /// </summary>
    /// <param name="obj">The object to compare with the current value object.</param>
    /// <returns>true if the specified object is equal to the current value object; otherwise, false.</returns>
    public override bool Equals(object? obj) => Equals(obj as ValueObject<TValue>);

    /// <summary>
    ///     Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current value object.</returns>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>
    ///     Returns a string that represents the current value object.
    /// </summary>
    /// <returns>A string that represents the current value object.</returns>
    public override string ToString() => Value.ToString();

    /// <summary>
    ///     Determines whether two specified value objects have the same value.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns>
    ///     true if the value of <paramref name="left" /> is the same as the value of <paramref name="right" />;
    ///     otherwise, false.
    /// </returns>
    public static bool operator ==(ValueObject<TValue>? left, ValueObject<TValue>? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        return EqualityComparer<ValueObject<TValue>>.Default.Equals(left!, right!);
    }

    /// <summary>
    ///     Determines whether two specified value objects have different values.
    /// </summary>
    /// <param name="left">The first value object to compare.</param>
    /// <param name="right">The second value object to compare.</param>
    /// <returns>
    ///     true if the value of <paramref name="left" /> is different from the value of <paramref name="right" />;
    ///     otherwise, false.
    /// </returns>
    public static bool operator !=(ValueObject<TValue>? left, ValueObject<TValue>? right) => !(left == right);

    /// <summary>
    ///     Performs an implicit conversion from <see cref="ValueObject{TValue}" /> to <typeparamref name="TValue" />.
    /// </summary>
    /// <param name="valueObject">The value object to convert.</param>
    /// <returns>The underlying value of the value object.</returns>
    public static implicit operator TValue(ValueObject<TValue> valueObject) => valueObject.Value;
}

using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

/// <summary>
/// A string at least <paramref name="length"/> characters long once trimmed. Passwords are hashed
/// trimmed, so <c>[MinLength]</c>, which measures the raw value, let "   abc    " through as ten
/// characters and stored three (#478 review).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TrimmedMinLengthAttribute(int length) : ValidationAttribute(
    $"The field must be at least {length} characters long, not counting leading or trailing spaces.")
{
    public int Length { get; } = length;

    public override bool IsValid(object? value) => value is string text && text.Trim().Length >= Length;
}

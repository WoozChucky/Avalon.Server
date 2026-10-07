using System.ComponentModel.DataAnnotations;
using Avalon.Domain.Characters;

namespace Avalon.Api.Contract;

/// <summary>
/// A character name that follows <see cref="CharacterName"/> (#757): 3 to 12 ASCII letters, checked as sent. A
/// regular-expression attribute, so the pattern is in the OpenAPI schema too; the check itself is
/// <see cref="CharacterName.IsValid"/>, which also refuses the trailing newline <c>$</c> would let through. No name
/// (null) passes: a PATCH that leaves the name alone sends none.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class CharacterNameRuleAttribute : RegularExpressionAttribute
{
    public CharacterNameRuleAttribute() : base(CharacterName.Pattern)
    {
        ErrorMessage = CharacterName.Requirement;
    }

    public override bool IsValid(object? value) => value is null || (value is string s && CharacterName.IsValid(s));
}

using System.ComponentModel.DataAnnotations;
using Avalon.Infrastructure.Login;

namespace Avalon.Api.Contract;

/// <summary>
/// A username that follows <see cref="UsernameRule"/>: 3 to 16 ASCII letters, digits or
/// underscores, checked as sent. A regular-expression attribute, so the pattern is in the OpenAPI
/// schema too; the check itself is <see cref="UsernameRule.IsValid"/>, which also refuses the
/// trailing newline <c>$</c> would let through.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class UsernameRuleAttribute : RegularExpressionAttribute
{
    public UsernameRuleAttribute() : base(UsernameRule.Pattern)
    {
        ErrorMessage = UsernameRule.Requirement;
    }

    public override bool IsValid(object? value) => value is null || (value is string s && UsernameRule.IsValid(s));
}

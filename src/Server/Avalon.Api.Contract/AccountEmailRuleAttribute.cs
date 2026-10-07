using System.ComponentModel.DataAnnotations;
using Avalon.Domain.Auth;

namespace Avalon.Api.Contract;

/// <summary>
/// Holds a request's email to <see cref="AccountEmail.IsValid"/> (#503 follow-up): an ASCII address
/// with one <c>@</c>. A 400 validation error otherwise. A missing value is left to <c>[Required]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class AccountEmailRuleAttribute : ValidationAttribute
{
    public AccountEmailRuleAttribute()
    {
        ErrorMessage = AccountEmail.Requirement;
    }

    public override bool IsValid(object? value) => value is null || (value is string s && AccountEmail.IsValid(s));
}

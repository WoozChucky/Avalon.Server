using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public class RegisterRequest
{
    // Owner decision (#487 re-review): 3 to 16 ASCII letters, digits or underscores, as sent.
    [Required, Avalon.Api.Contract.UsernameRuleAttribute] public string Username { get; set; } = string.Empty;

    // Measured trimmed, the form it is hashed in, as for a password change (#478 review).
    [Required, TrimmedMinLength(8)] public string Password { get; set; } = string.Empty;

    // An ASCII address (#503 follow-up), stored trimmed and lower-cased.
    [Required, EmailAddress, AccountEmailRule] public string Email { get; set; } = string.Empty;
}

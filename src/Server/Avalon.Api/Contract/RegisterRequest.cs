using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public class RegisterRequest
{
    [Required] public string Username { get; set; } = string.Empty;

    // Measured trimmed, the form it is hashed in, as for a password change (#478 review).
    [Required, TrimmedMinLength(8)] public string Password { get; set; } = string.Empty;

    [Required] public string Email { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class AccountPasswordChangeRequest
{
    [Required] public string CurrentPassword { get; set; } = "";
    // Measured trimmed: the hash is made from the trimmed password (#478 review).
    [Required, TrimmedMinLength(8)] public string NewPassword { get; set; } = "";
}

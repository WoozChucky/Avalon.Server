using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class AccountPasswordChangeRequest
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, MinLength(8)] public string NewPassword { get; set; } = "";
}

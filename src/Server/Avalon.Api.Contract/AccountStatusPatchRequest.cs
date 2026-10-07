using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class AccountStatusPatchRequest
{
    [Required] public AccountStatus State { get; set; }
    public string? Reason { get; set; }
}

using System.ComponentModel.DataAnnotations;
using Avalon.Domain.Auth;

namespace Avalon.Api.Contract;

public sealed class AccountStatusPatchRequest
{
    [Required] public AccountStatus State { get; set; }
    public string? Reason { get; set; }
}

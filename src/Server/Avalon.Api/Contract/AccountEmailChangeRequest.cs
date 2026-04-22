using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class AccountEmailChangeRequest
{
    [Required, EmailAddress] public string NewEmail { get; set; } = "";
}

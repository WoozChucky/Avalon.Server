using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class AccountEmailConfirmRequest
{
    [Required] public string Token { get; set; } = "";
}

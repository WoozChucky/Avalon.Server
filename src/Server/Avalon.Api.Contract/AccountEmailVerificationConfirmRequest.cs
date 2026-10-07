using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class AccountEmailVerificationConfirmRequest
{
    [Required, StringLength(43, MinimumLength = 43), RegularExpression("^[A-Za-z0-9_-]{43}$")]
    public string Token { get; set; } = "";
}

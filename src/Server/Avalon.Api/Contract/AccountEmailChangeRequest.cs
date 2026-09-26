using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class AccountEmailChangeRequest
{
    [Required, EmailAddress] public string NewEmail { get; set; } = "";

    /// <summary>
    /// The account's current password (#503): a session alone cannot start an email change. Missing
    /// or wrong is 401 <c>Invalid current password</c>, and counts as a failed login, as for
    /// <c>POST /account/password</c>, <c>POST /mfa/setup</c> and <c>POST /pat</c>.
    /// </summary>
    public string CurrentPassword { get; set; } = "";
}

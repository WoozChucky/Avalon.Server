using System.ComponentModel.DataAnnotations;

namespace Avalon.Api.Contract;

public sealed class AccountStatusPatchRequest
{
    /// <summary>A defined status only: a ban or a deactivation also ends every game session of the account (#882).</summary>
    [Required, EnumDataType(typeof(AccountStatus))] public AccountStatus State { get; set; }
    public string? Reason { get; set; }
}

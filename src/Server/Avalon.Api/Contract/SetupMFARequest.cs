namespace Avalon.Api.Contract;

/// <summary>
/// Starts MFA enrolment for the signed-in account (#478). The current password is required: a
/// session alone must not be able to enrol an authenticator the owner does not hold. A wrong one
/// counts as a failed login.
/// </summary>
public class SetupMFARequest
{
    public string CurrentPassword { get; set; } = string.Empty;
}

using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;

namespace Avalon.Infrastructure.Services;

public interface IMFAService
{
    Task<MFASetupResult> SetupMFAAsync(Account account, string issuer, CancellationToken cancellationToken = default);
    /// <summary>
    /// Confirms the account's pending setup. <paramref name="credentialsVersion"/> is the version
    /// the caller's session proved; a credentials change since refuses it (#495 re-review).
    /// </summary>
    Task<MFAConfirmResult> ConfirmMFAAsync(AccountId accountId, int credentialsVersion, string code,
        CancellationToken cancellationToken = default);
    Task<MFAVerifyResult> VerifyMFAAsync(string hash, string code, CancellationToken cancellationToken = default);
    /// <summary>
    /// The owner's MFA reset with the recovery codes. <paramref name="credentialsVersion"/> is the
    /// version the caller's session proved; a credentials change since refuses it (#495 re-review).
    /// </summary>
    Task<MFAResetResult> ResetMFAAsync(AccountId accountId, int credentialsVersion, string r1, string r2, string r3,
        CancellationToken cancellationToken = default);
    Task<bool> IsEnrolledAsync(AccountId accountId, CancellationToken cancellationToken = default);
}

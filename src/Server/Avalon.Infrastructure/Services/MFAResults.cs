using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Auth;

namespace Avalon.Infrastructure.Services;

public record MFASetupResult(bool Success, string? OtpUri, MFAOperationResult Status);
/// <param name="CredentialsChanged">
/// The account's credentials changed after the caller's session proved them (#495 re-review):
/// nothing was written, and the session should end.
/// </param>
public record MFAConfirmResult(bool Success, string[]? RecoveryCodes, MFAOperationResult Status,
    bool CredentialsChanged = false);

/// <summary>
/// The outcome of an MFA code. On a refusal, <paramref name="Refusal"/> says why, because only a
/// wrong code is a failed login: a replayed one proves nothing either way, and a hash another
/// caller spent first is not the code's fault (#478 review).
/// </summary>
public record MFAVerifyResult(bool Success, AccountId? AccountId, MfaCodeRefusal Refusal = MfaCodeRefusal.WrongCode);

/// <summary>Why a code was refused.</summary>
public enum MfaCodeRefusal
{
    /// <summary>The code is not valid in the window, or the account has no confirmed MFA. A failed login.</summary>
    WrongCode = 0,

    /// <summary>A right code whose step was already accepted. Not a failed login, and the hash is not spent.</summary>
    Replayed,

    /// <summary>The hash was spent by another verify first. Not a failed login.</summary>
    HashSpent,
}

/// <inheritdoc cref="MFAConfirmResult"/>
public record MFAResetResult(bool Success, MFAOperationResult Status, bool CredentialsChanged = false);

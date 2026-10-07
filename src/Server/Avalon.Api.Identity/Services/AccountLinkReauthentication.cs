using System.Net;
using Avalon.Common.GameAuth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;

namespace Avalon.Api.Identity.Services;

public sealed record LinkReauthenticated(string? Error, int CredentialsVersion, long SessionEpoch, Guid? ConfirmedMfaId);

/// <summary>A browser session or a refreshed JWT alone cannot grant store link consent.</summary>
public sealed class AccountLinkReauthentication(IReauthentication password, IMfaSetupRepository setups,
    IMFAHashService hashes, MfaLoginPolicy mfa)
{
    public async Task<LinkReauthenticated> RequireAsync(Account account, string currentPassword, string? code,
        IPAddress source, CancellationToken cancellationToken)
    {
        Reauthenticated proof = await password.RequireCurrentPasswordAsync(account.Id, currentPassword, source, cancellationToken);
        if (proof.AccountId != account.Id || proof.CredentialsVersion != account.CredentialsVersion)
            return new(GameAuthErrors.AccountUnavailable, 0, 0, null);
        MFASetup? setup = await setups.FindByAccountIdAsync(account.Id, cancellationToken);
        if (setup?.Status != MfaSetupStatus.Confirmed)
            return new(null, proof.CredentialsVersion, account.SessionEpoch, null);
        if (string.IsNullOrWhiteSpace(code)) return new(GameAuthErrors.MfaRequired, 0, 0, null);
        string hash = await hashes.GenerateHashAsync(account);
        MfaCodeAttempt attempt = await mfa.CheckAsync(hash, code, LoginSource.FromAddress(source), cancellationToken);
        if (attempt.Result != MfaCodeCheck.Correct)
        {
            if (attempt.Result == MfaCodeCheck.WrongCode) await mfa.RecordFailureAsync(attempt, cancellationToken);
            return new(GameAuthErrors.MfaInvalid, 0, 0, null);
        }
        if (attempt.Account?.Id != account.Id || attempt.Account.CredentialsVersion != proof.CredentialsVersion ||
            attempt.Account.SessionEpoch != account.SessionEpoch)
        {
            return new(GameAuthErrors.AccountUnavailable, 0, 0, null);
        }

        await mfa.GiveBackAsync(attempt);
        return new(null, proof.CredentialsVersion, account.SessionEpoch, setup.Id);
    }
}

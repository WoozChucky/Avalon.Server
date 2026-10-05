using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Common.ValueObjects;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Api.Exceptions;
using Avalon.Domain.Auth;
using AccountAccessLevel = Avalon.Common.Accounts.AccountAccessLevel;
using AccountStatus = Avalon.Domain.Auth.AccountStatus;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Avalon.Api.Services.Email;

public interface IAccountEmailVerificationService
{
    Task<AccountEmailVerificationStatusDto> GetStatusAsync(AccountId accountId, CancellationToken ct);
    Task RequestAsync(AccountId accountId, string sourceAddress, CancellationToken ct);
    Task ConfirmAsync(AccountId accountId, string token, CancellationToken ct);
}

public sealed class EmailVerificationUnavailableException() : Exception("Email verification is unavailable until email delivery is configured.");

public sealed class AccountEmailVerificationService(IAccountRepository accounts, IAccountEmailVerificationRepository challenges,
    IReplicatedCache cache, EmailConfig config, TimeProvider time, IEmailSender? sender = null,
    ILogger<AccountEmailVerificationService>? logger = null) : IAccountEmailVerificationService
{
    private bool DeliveryAvailable => sender is not null && config.VerificationSiteOrigin is not null;
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private static string Digest(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<AccountEmailVerificationStatusDto> GetStatusAsync(AccountId accountId, CancellationToken ct)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken: ct) ?? throw new BusinessException("Account unavailable.");
        var challenge = await challenges.FindAsync(accountId, ct);
        DateTime? resendAt = challenge?.IssuedAt.AddSeconds(config.VerificationCooldownSeconds);
        return new AccountEmailVerificationStatusDto
        {
            EmailVerifiedAt = account.EmailVerifiedAt, DeliveryAvailable = DeliveryAvailable,
            ResendAvailableAt = resendAt > Now ? resendAt : null,
        };
    }

    public async Task RequestAsync(AccountId accountId, string sourceAddress, CancellationToken ct)
    {
        if (!DeliveryAvailable) throw new EmailVerificationUnavailableException();
        var account = await accounts.FindByIdAsync(accountId, cancellationToken: ct) ?? throw new BusinessException("Account unavailable.");
        if (account.Email is null || !AccountEmail.IsValid(account.Email) || account.Status != AccountStatus.Active
            || (account.AccessLevel & AccountAccessLevel.Player) == 0 || account.IsLockedAt(Now) || account.GameplayConsolidationId is not null)
            throw new BusinessException("A current account email is required for verification.");
        if (account.EmailVerifiedAt is not null) return;
        if (!IPAddress.TryParse(sourceAddress, out var address)) throw new BusinessException("Source address unavailable.");
        // Failed sends and cooldown attempts keep their slots; no retry can amplify delivery indefinitely.
        var accountCount = await cache.IncrementAsync($"email-verification:account:{accountId.Value}", TimeSpan.FromHours(1));
        if (accountCount > config.MaxVerificationSendsPerAccount) throw new AccountLockedException();
        var sourceCount = await cache.IncrementAsync($"email-verification:source:{RemoteAddress.SourceOf(address)}", TimeSpan.FromHours(1));
        if (sourceCount > config.MaxVerificationSendsPerSource) throw new AccountLockedException();
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string digest = Digest(token);
        DateTime issuedAt = Now;
        var result = await challenges.IssueAsync(accountId, account.Email, account.CredentialsVersion, digest,
            issuedAt, issuedAt.AddMinutes(30), TimeSpan.FromSeconds(config.VerificationCooldownSeconds), ct);
        if (result == EmailVerificationIssueResult.AlreadyVerified) return;
        if (result == EmailVerificationIssueResult.Cooldown) throw new AccountLockedException();
        if (result != EmailVerificationIssueResult.Issued) throw new BusinessException("Account changed. Try again.");
        try
        {
            // Once committed, delivery and cleanup outlive an HTTP disconnect and have their own bounds.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            string link = $"{config.VerificationSiteOrigin!.TrimEnd('/')}/account/email/verify#token={token}";
            await sender!.SendAsync(account.Email, "Verify your Avalon email", $"Verify your current Avalon email address:\n{link}\nThis link expires in 30 minutes.", timeout.Token);
        }
        catch (Exception ex)
        {
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await challenges.InvalidateAsync(accountId, digest, cleanupTimeout.Token);
            }
            catch (Exception cleanup)
            {
                logger?.LogError("Could not invalidate a failed verification send for account {AccountId} ({ExceptionType})", accountId.Value, cleanup.GetType().Name);
            }
            logger?.LogWarning("Could not send verification for account {AccountId} ({ExceptionType})", accountId.Value, ex.GetType().Name);
            throw new EmailDeliveryException();
        }
    }

    public async Task ConfirmAsync(AccountId accountId, string token, CancellationToken ct)
    {
        if (token is null || token.Length != 43 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            || !await challenges.ConsumeAsync(accountId, Digest(token), Now, ct))
            throw new BusinessException("This verification link is invalid or expired. Request a new email.");
    }
}

using System.Net;
using System.Security.Authentication;
using System.Text;
using Avalon.Api.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Common.ValueObjects;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Auth;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using OperatingSystem = Avalon.Domain.Auth.OperatingSystem;

namespace Avalon.Api.Services;

public interface IAccountService
{
    Task<Account?> FindByIdAsync(AccountId id, CancellationToken cancellationToken = default);
    /// <summary>
    /// A password login. On a completed login <c>AccountId</c> is set, with the credentials version
    /// of the row the password matched: the refresh token is issued against it (#495).
    /// </summary>
    Task<(AuthenticateResponse Response, AccountId? AccountId, int CredentialsVersion)> Authenticate(AuthenticateRequest model, IPAddress ipAddress, CancellationToken cancellationToken);
    Task<(RegisterResponse Response, AccountId AccountId)> Register(RegisterRequest model, string userAgent, IPAddress ipAddress,
        CancellationToken cancellationToken);

    Task<PagedResult<Account>> Paginate(AccountPaginateFilters filters, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(AccountId accountId, string currentPassword, string newPassword, IPAddress ipAddress,
        CancellationToken cancellationToken = default);
    Task<string> InitiateEmailChangeAsync(AccountId accountId, string newEmail, CancellationToken cancellationToken = default);
    Task ConfirmEmailChangeAsync(string token, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(AccountId accountId, Avalon.Api.Contract.AccountStatus state, string? reason, AccountId actorId, CancellationToken cancellationToken = default);
    Task UpdateRolesAsync(AccountId accountId, Avalon.Api.Contract.AccountAccessLevel roles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes MFA from <paramref name="accountId"/> on behalf of admin <paramref name="actorId"/>.
    /// Returns <c>false</c> when the account does not exist. Once the database change commits it
    /// returns <c>true</c> even if the Redis cleanup or the world-disconnect publish fails; those
    /// are best-effort and only logged. Refusing an admin's own account is the caller's job.
    /// </summary>
    Task<bool> RemoveMfaAsync(AccountId accountId, AccountId actorId, CancellationToken cancellationToken = default);
}

public class AccountService : IAccountService
{
    private readonly ILogger<AccountService> _logger;
    private readonly IAccountRepository _accountRepository;
    private readonly IJwtUtils _jwtUtils;
    private readonly IMFAHashService _mfaHashService;
    private readonly IMfaSetupRepository _mfaSetupRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IReplicatedCache _cache;
    private readonly ISecureRandom _secureRandom;
    private readonly IRefreshTokenService _refreshService;
    private readonly IDbTransactionRunner<AuthDbContext> _authTransaction;
    private readonly AuthenticationConfig _authConfig;
    private readonly PasswordLoginPolicy _loginPolicy;
    private readonly IReauthentication _reauthentication;

    public AccountService(ILoggerFactory loggerFactory,
        IAccountRepository accountRepository,
        IJwtUtils jwtUtils,
        IMFAHashService mfaHashService,
        IMfaSetupRepository mfaSetupRepository,
        IDeviceRepository deviceRepository,
        IReplicatedCache cache,
        ISecureRandom secureRandom,
        IRefreshTokenService refreshService,
        IDbTransactionRunner<AuthDbContext> authTransaction,
        AuthenticationConfig authConfig,
        PasswordLoginPolicy loginPolicy,
        IReauthentication reauthentication)
    {
        _logger = loggerFactory.CreateLogger<AccountService>();
        _accountRepository = accountRepository;
        _jwtUtils = jwtUtils;
        _mfaHashService = mfaHashService;
        _mfaSetupRepository = mfaSetupRepository;
        _deviceRepository = deviceRepository;
        _cache = cache;
        _secureRandom = secureRandom;
        _refreshService = refreshService;
        _authTransaction = authTransaction;
        _authConfig = authConfig;
        _loginPolicy = loginPolicy;
        _reauthentication = reauthentication;
    }

    public async Task<Account?> FindByIdAsync(AccountId id, CancellationToken cancellationToken = default)
    {
        return await _accountRepository.FindByIdAsync(id, track: false, cancellationToken);
    }

    public async Task<(AuthenticateResponse Response, AccountId? AccountId, int CredentialsVersion)> Authenticate(AuthenticateRequest model, IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Password))
            throw new AuthenticationException(InvalidCredentials);

        // The game client's login policy (#478): the source's and the username's budgets, taken
        // before the lookup; a dummy BCrypt verify for an unknown username; the row's lock before
        // the password. Refusals past a budget, and a locked row, are 429 LOCKED for every username
        // alike. Every slot taken is kept unless the attempt ends below with an MFA hash or a
        // completed login.
        PasswordAttempt attempt = await _loginPolicy.CheckAsync(model.Username, model.Password,
            LoginSource.FromAddress(ipAddress), cancellationToken);

        if (attempt.Refused)
            throw new AccountLockedException();

        if (attempt.Failed)
        {
            // HTTP cannot answer before this write, as the game client's login does, so an
            // unknown username runs the same statements against an id no account has: known and
            // unknown usernames take the same path. The failure in the last slot locks the
            // account (or holds the unknown username's budget) and is answered as locked.
            await _loginPolicy.RecordFailureAsync(attempt, cancellationToken, writeWithoutAccount: true);
            throw FailureFor(attempt);
        }

        var account = attempt.Account!;

        // A banned or deactivated account gets nothing, not even an MFA hash (#480). Past the
        // password check it is told its status, as the game client is; a wrong password above
        // never learns it. Its slots stay taken, as a wrong password's do.
        if (!AccountAccessCheck.MayHoldSession(account))
            throw new AccountInactiveException(account.Status);

        var mfaSetup = await _mfaSetupRepository.FindByAccountIdAsync(account.Id, cancellationToken);
        if (mfaSetup is { Status: MfaSetupStatus.Confirmed })
        {
            // Only its own slots back, and no reset: the login completes at MFA verify.
            await _loginPolicy.GiveBackAsync(attempt);
            return (new AuthenticateResponse
            {
                Token = null,
                ExpiresAt = null,
                MfaHash = await _mfaHashService.GenerateHashAsync(account),
                Status = AuthenticationResponseStatus.RequiresMFA
            }, null, account.CredentialsVersion);
        }

        // By column, and only while the account is not locked (#484): a lock or a ban written
        // since the row was read survives. A lock that landed meanwhile is answered as a wrong
        // password in this slot would be, and the slots stay taken.
        if (!await _accountRepository.TryRecordApiLoginAsync(account.Id, attempt.Source.Ip, DateTime.UtcNow,
                cancellationToken))
        {
            _logger.LogWarning("Account {AccountId} was locked during its login", account.Id);
            throw FailureFor(attempt);
        }

        await _loginPolicy.CompleteAsync(attempt);

        return (new AuthenticateResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_authConfig.AccessTokenLifetimeMinutes).ToUnixTimeSeconds(),
            Status = AuthenticationResponseStatus.Success
        }, account.Id, account.CredentialsVersion);
    }

    private const string InvalidCredentials = "Invalid username or password";
    private const string UsernameTaken = "Username already exists";

    /// <summary>The answer to a failed password in this attempt's budget slot: locked in the last one.</summary>
    private Exception FailureFor(PasswordAttempt attempt) =>
        _loginPolicy.FailureLocks(attempt) ? new AccountLockedException() : new AuthenticationException(InvalidCredentials);

    public async Task<(RegisterResponse Response, AccountId AccountId)> Register(RegisterRequest model, string userAgent, IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        // Registration says whether a username or an email is taken, so it is budgeted like a
        // login (#495): a slot from the source's budget, the one logins spend over TCP and REST,
        // taken before any lookup. Past the budget the answer is 429 LOCKED, whatever the name.
        // A registration that creates the account gives its slot back; every other ending,
        // "already exists" included, keeps it, so a source can ask about only so many names.
        var sourceKey = await TakeRegistrationSlotAsync(ipAddress);

        var username = model.Username.ToUpperInvariant().Trim();
        var existingAccount = await _accountRepository.FindByUserNameAsync(username, cancellationToken);
        if (existingAccount != null)
            throw new BusinessException(UsernameTaken);

        existingAccount = await _accountRepository.FindByEmailAsync(model.Email, cancellationToken);
        if (existingAccount != null)
            throw new BusinessException("Email already exists");

        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(model.Password.Trim(), salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);

        var account = new Account
        {
            Username = username,
            Email = model.Email,
            Salt = saltBytes,
            Verifier = hashBytes,
            LastIp = ipAddress.ToString(),
            LastLogin = DateTime.UtcNow,
            JoinDate = DateTime.UtcNow,
            Locale = Avalon.Common.Accounts.AccountLocale.enUS,
            Os = OperatingSystem.Windows,
        };

        account = await InsertAccountAsync(account, ipAddress, cancellationToken);

        if (account == null)
            throw new Exception("Failed to insert account");

        await _deviceRepository.CreateAsync(new Device
        {
            AccountId = account.Id,
            Name = userAgent,
            LastUsage = DateTime.UtcNow,
            Trusted = false,
            TrustEnd = DateTime.UtcNow,
        }, cancellationToken);

        await SourceBudget.GiveBackAsync(_cache, sourceKey);

        return (new RegisterResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_authConfig.AccessTokenLifetimeMinutes).ToUnixTimeSeconds(),
        }, account.Id);
    }

    /// <summary>Takes the registration's slot from its source's budget (#495); 429 LOCKED past it.</summary>
    private async Task<string> TakeRegistrationSlotAsync(IPAddress ipAddress)
    {
        var sourceKey = LoginSource.FromAddress(ipAddress).Key;
        if (await SourceBudget.TryTakeAsync(_cache, _authConfig, sourceKey))
            return sourceKey;

        _logger.LogWarning("Registration refused for source {SourceKey}: too many attempts", sourceKey);
        throw new AccountLockedException();
    }

    /// <summary>
    /// Takes a slot of the source's account-creation cap (#495 review): at most
    /// <c>MaxAccountsCreatedPerSource</c> accounts per <c>AccountCreationWindowMinutes</c>. Unlike the
    /// login budget, a created account keeps its slot. Past the cap: 429 LOCKED.
    /// </summary>
    private async Task<string> TakeCreationSlotAsync(IPAddress ipAddress)
    {
        var key = CacheKeys.AuthSourceAccountsCreated(RemoteAddress.SourceOf(ipAddress));
        long created = await AttemptBudget.TakeAsync(_cache, key,
            TimeSpan.FromMinutes(_authConfig.AccountCreationWindowMinutes));
        if (created <= _authConfig.MaxAccountsCreatedPerSource)
            return key;

        await AttemptBudget.GiveBackAsync(_cache, key);
        _logger.LogWarning("Registration refused for source {SourceKey}: account creation cap reached", key);
        throw new AccountLockedException();
    }

    /// <summary>
    /// Inserts a new account. The "taken" check before it and this insert are not atomic: a
    /// registration of the same name can land in between, and the unique index on Username
    /// refuses this one (#487). Its caller gets the answer the check would have given; any other
    /// failure is rethrown. The insert takes a slot of the source's creation cap first, given back
    /// when no account comes of it.
    /// </summary>
    private async Task<Account> InsertAccountAsync(Account account, IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        var creationKey = await TakeCreationSlotAsync(ipAddress);
        try
        {
            return await _accountRepository.CreateAsync(account, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            await AttemptBudget.GiveBackAsync(_cache, creationKey);
            if (await _accountRepository.FindByUserNameAsync(account.Username, cancellationToken) != null)
                throw new BusinessException(UsernameTaken, ex);
            throw;
        }
        catch
        {
            await AttemptBudget.GiveBackAsync(_cache, creationKey);
            throw;
        }
    }

    public async Task<PagedResult<Account>> Paginate(AccountPaginateFilters filters, CancellationToken cancellationToken)
    {
        return await _accountRepository.PaginateAsync(filters, false, cancellationToken);
    }

    public async Task ChangePasswordAsync(AccountId accountId, string currentPassword, string newPassword, IPAddress ipAddress,
        CancellationToken cancellationToken = default)
    {
        // Through the login policy (#478): a stolen session guessing the current password here
        // spends the same budgets, and locks the same account, as guessing it at login.
        var proof = await _reauthentication.RequireCurrentPasswordAsync(accountId, currentPassword, ipAddress,
            cancellationToken);

        var salt = BCrypt.Net.BCrypt.GenerateSalt();
        var hash = BCrypt.Net.BCrypt.HashPassword(newPassword.Trim(), salt);
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        var hashBytes = Encoding.UTF8.GetBytes(hash);

        // One transaction: the password is written by column (#484), so a lock or a ban written
        // since the account was read survives it, and every refresh token and personal access
        // token the account holds is revoked with it (#483), so none minted with the old password,
        // or with a stolen session, outlives the change.
        var changed = await _authTransaction.ExecuteAsync(async (context, token) =>
        {
            // Only while still at the version the current password was checked at (#495 review).
            if (await AccountRepository.SetPasswordAsync(context, accountId, saltBytes, hashBytes,
                    proof.CredentialsVersion, token) == 0)
                return false;

            await RefreshTokenRepository.RevokeAllForAccountAsync(context, accountId, token);
            await PersonalAccessTokenRepository.RevokeAllForAccountAsync(context, accountId, accountId,
                DateTime.UtcNow, token);
            return true;
        }, cancellationToken);

        // The account is gone, or another credentials change landed since the password was checked.
        if (!changed)
            throw new AuthenticationException(RefreshTokenService.CredentialsChanged);

        // A login past its password step holds an MFA hash made with the old password (#495). Its
        // version no longer matches, so it cannot complete; clearing it also frees the account's
        // hash slot for the owner's next login. Best-effort, as after an admin's MFA removal.
        await ClearPendingMfaAsync(accountId, "its password was changed");
        await PublishDisconnectAsync(accountId, "its password was changed");
    }

    /// <summary>
    /// Clears the MFA state of a login already past its password step, and its reverse lookup, so
    /// that login cannot finish. The state also expires on its own short TTL. Best-effort: the
    /// change it follows is committed, so a Redis failure is logged and the call still succeeds.
    /// </summary>
    private async Task ClearPendingMfaAsync(AccountId accountId, string reason)
    {
        try
        {
            var mfaKey = CacheKeys.AccountMfa(accountId.Value);
            var pendingHash = await _cache.Database.HashGetAsync(mfaKey, "hash");
            if (pendingHash.HasValue)
                await _cache.RemoveAsync(CacheKeys.MfaReverseHash(pendingHash!));
            await _cache.RemoveAsync(mfaKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clear pending MFA state in Redis for account {AccountId} after {Reason}",
                accountId.Value, reason);
        }
    }

    /// <summary>
    /// Kicks any live world session of the account. Best-effort: the change it follows is committed,
    /// so a Redis failure is logged and the call still succeeds.
    /// </summary>
    private async Task PublishDisconnectAsync(AccountId accountId, string reason)
    {
        try
        {
            await _cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, accountId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not publish a world disconnect for account {AccountId} after {Reason}",
                accountId.Value, reason);
        }
    }

    public async Task<string> InitiateEmailChangeAsync(AccountId accountId, string newEmail,
        CancellationToken cancellationToken = default)
    {
        var raw = _secureRandom.GetBytes(24);
        var token = Convert.ToBase64String(raw).Replace("+", "-").Replace("/", "_").TrimEnd('=');

        var payload = $"{accountId.Value}|{newEmail}";
        var key = $"auth:emailChange:{token}";
        await _cache.SetAsync(key, payload, TimeSpan.FromMinutes(15));

        return token;
    }

    public async Task ConfirmEmailChangeAsync(string token, CancellationToken cancellationToken = default)
    {
        var key = $"auth:emailChange:{token}";
        var payload = await _cache.GetAsync(key)
            ?? throw new BusinessException("Invalid or expired token");
        // The DEL spends the token, not the GET (#478 review): two confirms can both read it, and
        // only the one whose delete removed it goes on.
        if (!await _cache.RemoveAsync(key))
            throw new BusinessException("Invalid or expired token");

        var parts = payload.Split('|', 2);
        if (parts.Length != 2)
            throw new BusinessException("Invalid token payload");

        var accountId = new AccountId(long.Parse(parts[0]));
        var newEmail = parts[1];

        // By column (#478): a lock or a ban written since the account was last read survives it.
        if (!await _accountRepository.SetEmailAsync(accountId, newEmail, cancellationToken))
            throw new BusinessException("Account not found");

        await _refreshService.RevokeAllForAccountAsync(accountId, cancellationToken);
        await PublishDisconnectAsync(accountId, "its email was changed");
    }

    // NOTE: `reason` is currently accepted but not persisted (future: audit log).
    public async Task UpdateStatusAsync(AccountId accountId, Avalon.Api.Contract.AccountStatus state, string? reason,
        AccountId actorId, CancellationToken cancellationToken = default)
    {
        // A repository call creates its own context, so a repository call cannot join a
        // transaction opened elsewhere. The three writes run on the one context this opens:
        // a ban that revoked no credentials would leave the banned account a live session.
        await _authTransaction.ExecuteAsync(async (context, token) =>
        {
            var account = await context.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, token)
                ?? throw new BusinessException("Account not found");

            account.Status = (Avalon.Domain.Auth.AccountStatus)state;
            await context.SaveChangesAsync(token);

            await RefreshTokenRepository.RevokeAllForAccountAsync(context, accountId, token);

            if (state is Avalon.Api.Contract.AccountStatus.Banned or Avalon.Api.Contract.AccountStatus.Deactivated)
            {
                await PersonalAccessTokenRepository.RevokeAllForAccountAsync(context, accountId, actorId,
                    DateTime.UtcNow, token);
            }
        }, cancellationToken);

        if (state is Avalon.Api.Contract.AccountStatus.Banned or Avalon.Api.Contract.AccountStatus.Deactivated)
        {
            await PublishDisconnectAsync(accountId, "its status changed to " + state);
        }
    }

    public async Task UpdateRolesAsync(AccountId accountId, Avalon.Api.Contract.AccountAccessLevel roles, CancellationToken cancellationToken = default)
    {
        // By column (#478): a lock or a ban written since the account was last read survives it.
        if (!await _accountRepository.SetAccessLevelAsync(accountId, (Avalon.Common.Accounts.AccountAccessLevel)roles,
                cancellationToken))
            throw new BusinessException("Account not found");
    }

    /// <inheritdoc />
    /// <remarks>
    /// The transaction starts by raising the credentials version (#495). It is also the existence
    /// check, and the row lock it takes orders this removal against a concurrent refresh rotation
    /// or token mint, as for a password change.
    /// </remarks>
    public async Task<bool> RemoveMfaAsync(AccountId accountId, AccountId actorId,
        CancellationToken cancellationToken = default)
    {
        // The only way back into an account whose authenticator is lost. The MFA row goes, and
        // every refresh token and personal access token goes with it in the same transaction, so
        // no session opened before the reset outlives it.
        var removed = await _authTransaction.ExecuteAsync(async (context, token) =>
        {
            if (await AccountRepository.BumpCredentialsVersionAsync(context, accountId, token) == 0)
                return (Found: false, Rows: 0);

            var rows = await MfaSetupRepository.DeleteAllForAccountAsync(context, accountId, token);
            await RefreshTokenRepository.RevokeAllForAccountAsync(context, accountId, token);
            await PersonalAccessTokenRepository.RevokeAllForAccountAsync(context, accountId, actorId,
                DateTime.UtcNow, token);

            return (Found: true, Rows: rows);
        }, cancellationToken);

        if (!removed.Found)
            return false;

        // The audit line goes first, straight after the commit: the reset has happened, and a
        // Redis failure below must not be able to lose the record of who did it.
        _logger.LogInformation(
            "Admin {ActorId} removed MFA from account {AccountId} ({Rows} row(s) deleted); its refresh and personal access tokens were revoked",
            actorId.Value, accountId.Value, removed.Rows);

        // Everything from here is best-effort. The database change is committed, so a Redis
        // failure is logged and the call still succeeds; each step is attempted on its own.
        await ClearPendingMfaAsync(accountId, "its MFA was removed");

        try
        {
            // Kick any live world session, the same way a ban or a password change does.
            await _cache.PublishAsync(CacheKeys.WorldAccountsDisconnectChannel, accountId.Value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not publish a world disconnect for account {AccountId} after its MFA was removed",
                accountId.Value);
        }

        return true;
    }
}

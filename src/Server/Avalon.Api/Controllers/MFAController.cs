using Avalon.Api.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Api.Exceptions;
using Avalon.Api.Services;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Avalon.Api.Controllers;

[Authorize]
[ApiController]
[Route("mfa")]
public class MFAController : BaseController
{
    private readonly IMFAService _mfaService;
    private readonly IAuthContext _authContext;
    private readonly AuthenticationConfig _authConfig;
    private readonly IJwtUtils _jwtUtils;
    private readonly IAccountRepository _accountRepository;
    private readonly IRefreshTokenService _refreshService;
    private readonly MfaLoginPolicy _mfaPolicy;
    private readonly IReauthentication _reauthentication;

    public MFAController(IMFAService mfaService, IAuthContext authContext, AuthenticationConfig authConfig,
        IJwtUtils jwtUtils, IAccountRepository accountRepository, IRefreshTokenService refreshService,
        MfaLoginPolicy mfaPolicy, IReauthentication reauthentication)
    {
        _mfaService = mfaService;
        _authContext = authContext;
        _authConfig = authConfig;
        _jwtUtils = jwtUtils;
        _accountRepository = accountRepository;
        _refreshService = refreshService;
        _mfaPolicy = mfaPolicy;
        _reauthentication = reauthentication;
    }

    [HttpPost("setup", Name = "Setup MFA for the logged account")]
    public async Task<ActionResult<SetupMFAResponse>> SetupMFA([FromBody] SetupMFARequest request)
    {
        // A session alone must not enrol an authenticator (#478): with a stolen access token and
        // no MFA yet, that locks the owner out of an account they can still log in to. The current
        // password is checked by the login policy, so a wrong one is a failed login.
        await _reauthentication.RequireCurrentPasswordAsync(_authContext.Account!.Id, request.CurrentPassword,
            SourceAddress, CancellationToken);

        var result = await _mfaService.SetupMFAAsync(_authContext.Account!, _authConfig.Issuer, CancellationToken);
        if (!result.Success)
            return Problem(result.Status.ToString(), statusCode: 400);
        return new SetupMFAResponse { Uri = result.OtpUri! };
    }

    [HttpPost("confirm", Name = "Confirm a MFA setup process for the logged account")]
    public async Task<ActionResult<ConfirmMFAResponse>> ConfirmMFA([FromBody] ConfirmMFARequest request)
    {
        var result = await _mfaService.ConfirmMFAAsync(_authContext.Account!.Id, request.Code, CancellationToken);
        if (!result.Success)
            return Problem(result.Status.ToString(), statusCode: 400);
        return new ConfirmMFAResponse
        {
            RecoveryCode1 = result.RecoveryCodes![0],
            RecoveryCode2 = result.RecoveryCodes[1],
            RecoveryCode3 = result.RecoveryCodes[2]
        };
    }

    /// <summary>
    /// Removes MFA with the recovery codes, and revokes the account's refresh tokens and personal
    /// access tokens. It enrols nothing (#478 review): the recovery codes are not the password, so
    /// the client enrols again with <c>POST /mfa/setup</c>, which asks for it.
    /// </summary>
    [HttpPost("reset", Name = "Reset MFA for the logged account")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetMFA([FromBody] ResetMFARequest request)
    {
        var account = _authContext.Account!;
        var reset = await _mfaService.ResetMFAAsync(account.Id, request.RecoveryCode1, request.RecoveryCode2, request.RecoveryCode3, CancellationToken);
        if (!reset.Success)
            return Problem(reset.Status.ToString(), statusCode: 400);

        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("verify", Name = "Verify MFA for the logged account")]
    public async Task<ActionResult<AuthenticateResponse>> VerifyMFA([FromBody] VerifyMFARequest request)
    {
        // The game client's MFA policy (#478): the source's budget, the hash's attempt count (the
        // last wrong code deletes it), the account's username budget and its lock, all before the
        // code is checked; and only one caller can win a hash, so two parallel verifies of one
        // hash cannot both get a session.
        MfaCodeAttempt attempt = await _mfaPolicy.CheckAsync(request.Hash, request.Code,
            LoginSource.FromAddress(SourceAddress), CancellationToken);

        switch (attempt.Result)
        {
            case MfaCodeCheck.SourceRefused or MfaCodeCheck.UsernameRefused or MfaCodeCheck.Locked:
                throw new AccountLockedException();
            case MfaCodeCheck.HashGone or MfaCodeCheck.HashSpent or MfaCodeCheck.AccountMissing or MfaCodeCheck.Replayed:
                return InvalidCode();
            case MfaCodeCheck.WrongCode:
                // Counted on the row; in the budget's last slot it locks the account.
                await _mfaPolicy.RecordFailureAsync(attempt, CancellationToken);
                return FailureFor(attempt);
        }

        var account = attempt.Account!;

        // The code was right, so the caller holds the account: a banned or deactivated one is
        // told its status, as at login, and gets no session (#480). The hash outlives the password
        // step by two minutes, so a ban inside that window is caught here. Its slots stay taken.
        if (!AccountAccessCheck.MayHoldSession(account))
            throw new AccountInactiveException(account.Status);

        // By column, and only while the account is not locked (#484): a lock or a ban written
        // since the row was read survives, and a lock that landed meanwhile gets a wrong code's
        // answer, with the slots kept.
        if (!await _accountRepository.TryRecordApiLoginAsync(account.Id, attempt.Source.Ip, DateTime.UtcNow,
                CancellationToken))
            return FailureFor(attempt);

        await _mfaPolicy.CompleteAsync(attempt);

        // The account was read by the policy, which checked its version against the hash's (#495).
        var issue = await _refreshService.IssueAsync(account.Id, account.CredentialsVersion, CancellationToken);
        SetRefreshCookie(issue.RawToken, issue.ExpiresAt, _authConfig);

        return new AuthenticateResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_authConfig.AccessTokenLifetimeMinutes).ToUnixTimeSeconds(),
            Status = AuthenticationResponseStatus.Success
        };
    }

    private ActionResult InvalidCode() => Problem("Invalid MFA code or expired hash", statusCode: 401);

    /// <summary>A wrong code in this attempt's budget slot: 401, or 429 LOCKED in the last one.</summary>
    private ActionResult FailureFor(MfaCodeAttempt attempt) =>
        _mfaPolicy.FailureLocks(attempt) ? throw new AccountLockedException() : InvalidCode();

    [HttpGet("status", Name = "Get MFA status for the logged account")]
    [ProducesResponseType(typeof(MfaStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MfaStatusResponse>> GetStatus()
    {
        var enrolled = await _mfaService.IsEnrolledAsync(_authContext.Account!.Id, CancellationToken);
        return new MfaStatusResponse { Enrolled = enrolled };
    }
}

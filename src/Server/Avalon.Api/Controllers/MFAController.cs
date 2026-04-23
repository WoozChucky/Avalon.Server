using Avalon.Api.Authentication;
using Avalon.Api.Authentication.Jwt;
using Avalon.Api.Config;
using Avalon.Api.Contract;
using Avalon.Database.Auth.Repositories;
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

    public MFAController(IMFAService mfaService, IAuthContext authContext, AuthenticationConfig authConfig,
        IJwtUtils jwtUtils, IAccountRepository accountRepository)
    {
        _mfaService = mfaService;
        _authContext = authContext;
        _authConfig = authConfig;
        _jwtUtils = jwtUtils;
        _accountRepository = accountRepository;
    }

    [HttpGet("setup", Name = "Setup MFA for the logged account")]
    public async Task<ActionResult<SetupMFAResponse>> SetupMFA()
    {
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

    [HttpPost("reset", Name = "Reset MFA for the logged account")]
    public async Task<ActionResult<SetupMFAResponse>> ResetMFA([FromBody] ResetMFARequest request)
    {
        var account = _authContext.Account!;
        var reset = await _mfaService.ResetMFAAsync(account.Id, request.RecoveryCode1, request.RecoveryCode2, request.RecoveryCode3, CancellationToken);
        if (!reset.Success)
            return Problem(reset.Status.ToString(), statusCode: 400);

        var setup = await _mfaService.SetupMFAAsync(account, _authConfig.Issuer, CancellationToken);
        return new SetupMFAResponse { Uri = setup.OtpUri! };
    }

    [AllowAnonymous]
    [HttpPost("verify", Name = "Verify MFA for the logged account")]
    public async Task<ActionResult<AuthenticateResponse>> VerifyMFA([FromBody] VerifyMFARequest request)
    {
        var result = await _mfaService.VerifyMFAAsync(request.Hash, request.Code, CancellationToken);
        if (!result.Success)
            return Problem("Invalid MFA code or expired hash", statusCode: 401);

        var account = await _accountRepository.FindByIdAsync(result.AccountId!, false, CancellationToken);
        if (account == null)
            return Problem("Account not found", statusCode: 401);

        account.LastIp = IpAddress.ToString();
        account.LastLogin = DateTime.UtcNow;
        await _accountRepository.UpdateAsync(account, CancellationToken);

        return new AuthenticateResponse
        {
            Token = _jwtUtils.GenerateJwtToken(account),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_authConfig.AccessTokenLifetimeMinutes).ToUnixTimeSeconds(),
            Status = AuthenticationResponseStatus.Success
        };
    }

    [HttpGet("status", Name = "Get MFA status for the logged account")]
    [ProducesResponseType(typeof(MfaStatusResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MfaStatusResponse>> GetStatus()
    {
        var enrolled = await _mfaService.IsEnrolledAsync(_authContext.Account!.Id, CancellationToken);
        return new MfaStatusResponse { Enrolled = enrolled };
    }
}

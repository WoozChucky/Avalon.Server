using Avalon.Api.Authentication;
using Avalon.Api.Contract;
using Avalon.Api.Services;
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

    public MFAController(IMFAService mfaService, IAuthContext authContext)
    {
        _mfaService = mfaService;
        _authContext = authContext;
    }
    
    [HttpGet("setup", Name = "Setup MFA for the logged account")]
    public async Task<SetupMFAResponse> SetupMFA()
    {
        return await _mfaService.Setup2FA(_authContext.Account!, CancellationToken);
    }
    
    [HttpPost("confirm", Name = "Confirm a MFA setup process for the logged account")]
    public async Task<ConfirmMFAResponse> ConfirmMFA([FromBody] ConfirmMFARequest request)
    {
        return await _mfaService.ConfirmMFA(request.Code, _authContext.Account!, CancellationToken);
    }
    
    [HttpPost("reset", Name = "Reset MFA for the logged account")]
    public async Task<SetupMFAResponse> ResetMFA([FromBody] ResetMFARequest request)
    {
        return await _mfaService.ResetMFA(request, _authContext.Account!, CancellationToken);
    }
    
    [HttpPost("verify", Name = "Verify MFA for the logged account")]
    public async Task VerifyMFA([FromBody] VerifyMFARequest request)
    {
        await _mfaService.VerifyMFA(request.Code, _authContext.Account!, CancellationToken);
    }
}

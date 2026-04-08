using Avalon.Common.ValueObjects;
using Avalon.Domain.Auth;

namespace Avalon.Infrastructure.Services;

public interface IMFAService
{
    Task<MFASetupResult> SetupMFAAsync(Account account, string issuer, CancellationToken cancellationToken = default);
    Task<MFAConfirmResult> ConfirmMFAAsync(AccountId accountId, string code, CancellationToken cancellationToken = default);
    Task<MFAVerifyResult> VerifyMFAAsync(string hash, string code, CancellationToken cancellationToken = default);
    Task<MFAResetResult> ResetMFAAsync(AccountId accountId, string r1, string r2, string r3, CancellationToken cancellationToken = default);
}

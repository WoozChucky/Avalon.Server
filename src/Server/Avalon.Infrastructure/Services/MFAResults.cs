using Avalon.Common.ValueObjects;
using Avalon.Network.Packets.Auth;

namespace Avalon.Infrastructure.Services;

public record MFASetupResult(bool Success, string? OtpUri, MFAOperationResult Status);
public record MFAConfirmResult(bool Success, string[]? RecoveryCodes, MFAOperationResult Status);
public record MFAVerifyResult(bool Success, AccountId? AccountId);
public record MFAResetResult(bool Success, MFAOperationResult Status);

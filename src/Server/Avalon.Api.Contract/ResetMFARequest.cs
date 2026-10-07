namespace Avalon.Api.Contract;

public class ResetMFARequest
{
    public string RecoveryCode1 { get; set; } = string.Empty;
    public string RecoveryCode2 { get; set; } = string.Empty;
    public string RecoveryCode3 { get; set; } = string.Empty;
}

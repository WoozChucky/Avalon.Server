namespace Avalon.Api.Contract;

public class VerifyMFARequest
{
    public string Code { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

namespace Avalon.Api.Contract;

public sealed class AccountEmailVerificationStatusDto
{
    public DateTime? EmailVerifiedAt { get; set; }
    public bool DeliveryAvailable { get; set; }
    public DateTime? ResendAvailableAt { get; set; }
}

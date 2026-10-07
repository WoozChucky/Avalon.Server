namespace Avalon.Api.Contract;

public class PushSubscriptionRequest
{
    public string Endpoint { get; set; } = string.Empty;
    public long? ExpirationTime { get; set; }
    public PushSubscriptionKeys Keys { get; set; } = new();
}

public class PushSubscriptionKeys
{
    public string P256DH { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
}

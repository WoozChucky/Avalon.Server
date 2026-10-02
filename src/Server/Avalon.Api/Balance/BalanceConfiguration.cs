namespace Avalon.Api.Balance;

/// <summary>
/// The <c>Application:Balance</c> section: the in-cluster balance service and the secret shared with it.
/// Left empty, the <c>/balance</c> endpoints answer 503 and the rest of the API is unaffected.
/// </summary>
public sealed class BalanceConfiguration
{
    /// <summary>The service's in-cluster address, e.g. <c>http://avalon-balance:8080</c>.</summary>
    public string Url { get; set; } = "";

    /// <summary>Sent as <c>X-Balance-Secret</c>. Never logged, and never part of a response or an exception.</summary>
    public string SharedSecret { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(SharedSecret);
}

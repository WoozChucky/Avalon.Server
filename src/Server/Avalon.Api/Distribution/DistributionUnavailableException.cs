namespace Avalon.Api.Distribution;

/// <summary>No store is configured, or what it holds is not usable. The API answers 503.</summary>
public sealed class DistributionUnavailableException : Exception
{
    public DistributionUnavailableException(string message) : base(message) { }

    public DistributionUnavailableException(string message, Exception inner) : base(message, inner) { }
}

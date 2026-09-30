namespace Avalon.Api.Balance;

/// <summary>The balance service is not configured or cannot be reached. The API answers 503.</summary>
public sealed class BalanceUnavailableException : Exception
{
    public BalanceUnavailableException(string message) : base(message) { }

    public BalanceUnavailableException(string message, Exception inner) : base(message, inner) { }
}

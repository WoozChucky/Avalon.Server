namespace Avalon.Api.Identity.LoadTest;

/// <summary>
/// A load-test request refused by the state of the accounts: a run past <see cref="LoadTestOptions.MaxAccounts"/>, or a
/// run id already used. Answered as 409 ProblemDetails with the message as its detail; it never carries a password.
/// </summary>
public sealed class LoadTestConflictException : Exception
{
    public LoadTestConflictException(string message) : base(message)
    {
    }

    public LoadTestConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

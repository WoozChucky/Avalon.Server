namespace Avalon.Infrastructure.Login;

/// <summary>
/// The limits of the login policy both servers enforce: the game client's TCP login (Auth server,
/// under <c>Application</c>) and the REST API's (under <c>Application:Authentication</c>). The
/// budgets they govern are the same Redis keys on both, so both hosts must be given the same
/// values, or one key is judged against two different limits.
/// </summary>
public interface ILoginLimits
{
    /// <summary>Password and MFA-code attempts one username allows per window, from every source.</summary>
    int MaxFailedLoginAttempts { get; }

    /// <summary>How long an account stays locked once it reaches <see cref="MaxFailedLoginAttempts"/>; also that budget's window.</summary>
    int LockoutDurationMinutes { get; }

    /// <summary>Attempts one source may make, across every account, within <see cref="FailedLoginSourceWindowMinutes"/>.</summary>
    int MaxFailedLoginsPerSource { get; }

    /// <summary>The window, fixed from a source's first attempt, over which its attempts are counted.</summary>
    int FailedLoginSourceWindowMinutes { get; }

    /// <summary>Code attempts one MFA hash allows before it is deleted.</summary>
    int MaxFailedMfaAttempts { get; }
}

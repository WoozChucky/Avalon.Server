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

/// <summary>Startup check for a host that binds its <see cref="ILoginLimits"/> without validated options.</summary>
public static class LoginLimitsValidation
{
    /// <summary>
    /// Throws, naming the setting, when a limit is below one: a limit of zero would refuse every
    /// login, or never count one. <paramref name="section"/> is where the host binds the limits.
    /// </summary>
    public static void Validate(ILoginLimits limits, string section)
    {
        Require(limits.MaxFailedLoginAttempts, nameof(ILoginLimits.MaxFailedLoginAttempts), section);
        Require(limits.LockoutDurationMinutes, nameof(ILoginLimits.LockoutDurationMinutes), section);
        Require(limits.MaxFailedLoginsPerSource, nameof(ILoginLimits.MaxFailedLoginsPerSource), section);
        Require(limits.FailedLoginSourceWindowMinutes, nameof(ILoginLimits.FailedLoginSourceWindowMinutes), section);
        Require(limits.MaxFailedMfaAttempts, nameof(ILoginLimits.MaxFailedMfaAttempts), section);
    }

    /// <summary>
    /// Logs the five limits at Information (#478 review). The Auth server and the API count on the
    /// same Redis keys but are configured apart, so their startup lines are how a drift is seen.
    /// </summary>
    public static void LogAtStartup(Microsoft.Extensions.Logging.ILogger logger, ILoginLimits limits, string section)
    {
        Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(logger,
            "Login limits ({Section}): MaxFailedLoginAttempts={MaxFailedLoginAttempts}, " +
            "LockoutDurationMinutes={LockoutDurationMinutes}, MaxFailedLoginsPerSource={MaxFailedLoginsPerSource}, " +
            "FailedLoginSourceWindowMinutes={FailedLoginSourceWindowMinutes}, MaxFailedMfaAttempts={MaxFailedMfaAttempts}. " +
            "The Auth server and the API must agree on these",
            section, limits.MaxFailedLoginAttempts, limits.LockoutDurationMinutes, limits.MaxFailedLoginsPerSource,
            limits.FailedLoginSourceWindowMinutes, limits.MaxFailedMfaAttempts);
    }

    private static void Require(int value, string name, string section)
    {
        if (value < 1)
            throw new InvalidOperationException($"{section}:{name} must be at least 1 (it is {value}).");
    }
}

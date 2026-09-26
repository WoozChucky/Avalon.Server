using Avalon.Api.Config;
using Avalon.Api.Services;
using Avalon.Database.Auth.Repositories;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Login;
using Avalon.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avalon.Api.UnitTests.Services;

/// <summary>The shared login policy (#478) as the api composes it, over the collaborators a test hands in.</summary>
internal static class TestLogin
{
    public static PasswordLoginPolicy Password(IAccountRepository accounts, IReplicatedCache cache,
        ILoginLimits? limits = null, IPasswordVerifier? verifier = null) =>
        new(accounts, cache, limits ?? new AuthenticationConfig(), verifier ?? new BCryptPasswordVerifier(),
            NullLoggerFactory.Instance);

    public static MfaLoginPolicy Mfa(IAccountRepository accounts, IReplicatedCache cache, IMFAService mfa,
        IMFAHashService hashes, ILoginLimits? limits = null) =>
        new(accounts, cache, limits ?? new AuthenticationConfig(), mfa, hashes, NullLoggerFactory.Instance);

    public static Reauthentication Reauthentication(IAccountRepository accounts, IReplicatedCache cache,
        ILoginLimits? limits = null, IPasswordVerifier? verifier = null) =>
        new(accounts, Password(accounts, cache, limits, verifier));
}

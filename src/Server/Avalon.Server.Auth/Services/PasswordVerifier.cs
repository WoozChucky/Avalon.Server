namespace Avalon.Server.Auth.Services;

/// <summary>Checks a login password against a stored BCrypt hash.</summary>
public interface IPasswordVerifier
{
    bool Verify(string password, string hash);
}

public sealed class BCryptPasswordVerifier : IPasswordVerifier
{
    /// <summary>
    /// A hash no password is expected to match, verified against when a login names an unknown
    /// username so that it costs one BCrypt verify, the same as a wrong password on a real account
    /// (#471). Made at the library's default work factor, the one registration hashes with.
    /// </summary>
    public static readonly string UnknownAccountHash =
        BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), BCrypt.Net.BCrypt.GenerateSalt());

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}

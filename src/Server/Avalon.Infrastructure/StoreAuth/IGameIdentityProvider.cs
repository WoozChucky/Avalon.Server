using Avalon.Configuration;

namespace Avalon.Infrastructure.StoreAuth;

public sealed record GameIdentityProofRequest(GameApplicationSelection Application, string ExpectedChallenge, string Proof)
{
    public override string ToString() => $"Identity proof for {Application.Key} (proof redacted)";
}
public sealed record VerifiedGameIdentity(string ProviderSubject, DateTime VerifiedAt, DateTime ValidUntil);
public enum GameIdentityProofStatus { Verified, Invalid, Unavailable }
public sealed record GameIdentityProofResult(GameIdentityProofStatus Status, VerifiedGameIdentity? Identity = null);

public interface IGameIdentityProvider
{
    string Provider { get; }
    string CreateChallenge(GameApplicationSelection application);
    Task<GameIdentityProofResult> VerifyAsync(GameIdentityProofRequest request, CancellationToken ct);
}

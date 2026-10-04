using Avalon.Database.Auth.Repositories;
namespace Avalon.Infrastructure.GameAuth;
public interface IGameAccountRegistration
{
    Task<IdentityLinkResult> CreateFromSteamAsync(Guid operationId, string verifiedSteamId,
        DateTime proofExpiresAt, string sourceAddress, CancellationToken cancellationToken);
}

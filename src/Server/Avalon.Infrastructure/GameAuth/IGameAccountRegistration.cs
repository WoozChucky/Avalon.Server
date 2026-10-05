using Avalon.Database.Auth.Repositories;
namespace Avalon.Infrastructure.GameAuth;
public interface IGameAccountRegistration
{
    Task<IdentityLinkResult> CreateFromStoreAsync(Guid operationId, string provider, string verifiedSubject,
        DateTime proofExpiresAt, string sourceAddress, CancellationToken cancellationToken);
}

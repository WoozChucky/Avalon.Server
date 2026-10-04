using Avalon.Common.ValueObjects;

namespace Avalon.Infrastructure.GameTickets;

public sealed record GameTicketGrant(AccountId AccountId, Guid FamilyId, int CredentialsVersion,
    long SessionEpoch = 0, string Environment = "production");

public interface IGameTicketStore
{
    Task<string> IssueAsync(GameTicketGrant grant, CancellationToken cancellationToken);
    Task<GameTicketGrant?> RedeemAsync(string ticket, CancellationToken cancellationToken);
}

using Avalon.Database.Auth.Repositories;
using Avalon.Domain.Commerce;
using Microsoft.Extensions.Options;

namespace Avalon.Api.Commerce;

public enum NotificationAcceptance { Accepted, Invalid, Disabled }
public interface IPaymentNotificationService
{
    Task<NotificationAcceptance> AcceptAsync(string provider, ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, string> headers, CancellationToken ct);
}
public sealed class PaymentNotificationService(IPurchaseRepository purchases, PaymentProviderRegistry providers,
    IOptions<CommerceConfiguration> options, TimeProvider clock) : IPaymentNotificationService
{
    public async Task<NotificationAcceptance> AcceptAsync(string name, ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        var config = options.Value;
        if (!config.Enabled || name != config.Provider || providers.Find(name) is not { } provider) return NotificationAcceptance.Disabled;
        if (body.Length is 0 or > CommercePolicy.MaximumNotificationBytes) return NotificationAcceptance.Invalid;
        VerifiedPaymentNotification proof;
        try { proof = provider.VerifyNotification(body, headers, clock.GetUtcNow().UtcDateTime); }
        catch (PaymentProviderException) { return NotificationAcceptance.Invalid; }
        if (proof.Provider != config.Provider || proof.ProviderAccountId != config.ProviderAccountId || proof.PaymentEnvironment != config.PaymentEnvironment ||
            proof.CreatedAt.Kind != DateTimeKind.Utc || proof.ResourceKind is not (PaymentResourceKinds.Checkout or PaymentResourceKinds.Refund or PaymentResourceKinds.Dispute))
            return NotificationAcceptance.Invalid;
        var attempt = await purchases.ResolveAttemptAsync(proof.Provider, proof.ProviderAccountId, proof.PaymentEnvironment, proof.OrderId, proof.AttemptId,
            proof.ResourceKind == PaymentResourceKinds.Checkout ? proof.ResourceReference : null, proof.PaymentReference, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var row = new PaymentEvent { Id = Guid.NewGuid(), Provider = proof.Provider, ProviderAccountId = proof.ProviderAccountId, Environment = proof.PaymentEnvironment,
            ExternalReference = proof.EventReference, Type = proof.Type, ResourceKind = proof.ResourceKind, ResourceReference = proof.ResourceReference,
            PaymentReference = proof.PaymentReference, OrderId = attempt?.OrderId, PaymentAttemptId = attempt?.Id, CreatedAt = proof.CreatedAt, NextAttemptAt = now };
        // False means the verified event was already durable. Database failures propagate; they are never acknowledged.
        await purchases.AcceptEventAsync(row, ct);
        return NotificationAcceptance.Accepted;
    }
}

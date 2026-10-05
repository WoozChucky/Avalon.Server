using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

/// <summary>Historical provider evidence. A successful login, or an old positive observation, is not current ownership.</summary>
public sealed class LicenseObservation
{
    public Guid Id { get; set; }
    public Guid? LicenseId { get; set; }
    public long? AuthorityRevision { get; set; }
    public AccountId AccountId { get; set; } = null!;
    public required string Provider { get; set; }
    public required string ProviderSubject { get; set; }
    public required string Environment { get; set; }
    public required string Product { get; set; }
    public required string ProviderProductId { get; set; }
    public string? ProviderOwnerSubject { get; set; }
    public bool OwnsProduct { get; set; }
    public bool? Permanent { get; set; }
    public DateTime ObservedAt { get; set; }
    public DateTime AuthorizedUntil { get; set; }
    public DateTime? ProviderExpiresAt { get; set; }
    public int PolicyVersion { get; set; }

    public bool Authorizes(string environment, string product, DateTime utcNow) =>
        OwnsProduct && string.Equals(Environment, environment, StringComparison.Ordinal) &&
        string.Equals(Product, product, StringComparison.Ordinal) && ObservedAt <= utcNow &&
        utcNow < AuthorizedUntil && utcNow < ObservedAt.AddMinutes(5) &&
        (ProviderExpiresAt is null || utcNow < ProviderExpiresAt);
}

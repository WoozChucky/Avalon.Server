using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

/// <summary>The exact provider identity linked to an Avalon account. Never use the license owner's identity here.</summary>
public sealed class ExternalIdentity
{
    public Guid Id { get; set; }
    public AccountId AccountId { get; set; } = null!;
    public required string Provider { get; set; }
    public required string ProviderSubject { get; set; }
    public DateTime LinkedAt { get; set; }
}

namespace Avalon.Api.Commerce;

/// <summary>Configuration rules supplied by an adapter without constructing a credential-bearing client.</summary>
public sealed record PaymentProviderRegistration(string Name, Predicate<CommerceConfiguration> SettingsAreValid);

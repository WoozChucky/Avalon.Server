namespace Avalon.Api.Commerce;

public sealed class PaymentProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IPaymentProvider> _providers;
    public PaymentProviderRegistry(IEnumerable<IPaymentProvider> providers) =>
        _providers = providers.ToDictionary(x => x.Provider, StringComparer.Ordinal);
    public IPaymentProvider? Find(string provider) => _providers.GetValueOrDefault(provider);
}

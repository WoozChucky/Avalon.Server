using Avalon.Api.Commerce;
using Avalon.Domain.Commerce;
using Xunit;

namespace Avalon.Api.UnitTests.Commerce;

public sealed class PaymentProviderContractShould
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Alternative_provider_exposes_the_same_payment_lifecycle_without_Stripe_types(bool paid, bool refunded, bool disputed)
    {
        IPaymentProvider stripe = StripePaymentProviderShould.Provider(new StripePaymentProviderShould.Transport { Paid = paid, Refunded = refunded, Disputed = disputed });
        var other = new AlternativeProvider(paid, refunded, disputed);
        var registry = new PaymentProviderRegistry([stripe, other]);
        var actual = await registry.Find("stripe")!.GetCheckoutAsync(new("cs_test", null), default);
        var alternative = await registry.Find("alternative")!.GetCheckoutAsync(new("checkout", null), default);
        Assert.Equal(alternative.State, actual.State);
        Assert.Equal(alternative.Paid, actual.Paid);
        Assert.Equal(alternative.AmountMinor, actual.AmountMinor);
        Assert.Equal(alternative.Currency, actual.Currency);
        Assert.Equal(alternative.Quantity, actual.Quantity);
        Assert.Equal(alternative.Refunds.Select(x => x.State), actual.Refunds.Select(x => x.State));
        Assert.Equal(alternative.Disputes.Select(x => x.State), actual.Disputes.Select(x => x.State));
        Assert.Null(registry.Find("unknown"));
    }

    private sealed class AlternativeProvider(bool paid, bool refunded, bool disputed) : IPaymentProvider
    {
        public string Provider => "alternative";
        public Task<PaymentSnapshot> GetCheckoutAsync(PaymentLookup lookup, CancellationToken ct) => Task.FromResult(new PaymentSnapshot(
            Provider, "merchant", "sandbox", StripePaymentProviderShould.Order, StripePaymentProviderShould.Attempt,
            "checkout", "payment", "offer", "game", 1, 800, "eur", 0, 800, true, paid,
            paid ? PaymentAttemptState.Paid : PaymentAttemptState.Processing, StripePaymentProviderShould.Now.AddMinutes(30),
            [new("refund-one", "payment", 800, "eur", refunded ? PaymentRefundState.Succeeded : PaymentRefundState.Failed),
             new("refund-two", "payment", 800, "eur", refunded ? PaymentRefundState.Succeeded : PaymentRefundState.Failed)],
            disputed ? [new("dispute", "payment", 800, "eur", PaymentDisputeState.Open)] : []));
        public Task<CheckoutProviderResult> CreateCheckoutAsync(CheckoutCreateCommand command, CancellationToken ct) => throw new NotSupportedException();
        public VerifiedPaymentNotification VerifyNotification(ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, string> headers, DateTime now) => throw new NotSupportedException();
        public Task<RefundProviderResult> RequestFullRefundAsync(FullRefundCommand command, CancellationToken ct) => throw new NotSupportedException();
    }
}

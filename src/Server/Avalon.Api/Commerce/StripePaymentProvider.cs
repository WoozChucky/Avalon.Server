using System.Text;
using System.Text.Json;
using Avalon.Domain.Commerce;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Avalon.Api.Commerce;

public sealed class StripePaymentProvider(IOptions<CommerceConfiguration> options, StripeClient client, TimeProvider clock) : IPaymentProvider
{
    public const string ApiVersion = "2026-09-30.endive";
    public const string ProviderName = "stripe";
    public static PaymentProviderRegistration Registration => new(ProviderName, config =>
        CommerceOptionsValidator.Identifier(config.ProviderPriceId, "price_") && CommerceOptionsValidator.Identifier(config.ProviderCatalogProductId, "prod_") &&
        CommerceOptionsValidator.Identifier(config.ProviderAccountId, "acct_") &&
        (CommerceOptionsValidator.Identifier(config.ApiKey, "sk_test_") || CommerceOptionsValidator.Identifier(config.ApiKey, "rk_test_")) &&
        CommerceOptionsValidator.Identifier(config.WebhookSecret, "whsec_"));
    public string Provider => ProviderName;
    private CommerceConfiguration Configuration => options.Value;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Task<CheckoutProviderResult> CreateCheckoutAsync(CheckoutCreateCommand command, CancellationToken ct) => Safe(async () =>
    {
        Enabled();
        if (command.OrderId == Guid.Empty || command.AttemptId == Guid.Empty || !CommerceOptionsValidator.Text(command.OperationKey, 128) ||
            command.AmountMinor <= 0 || !CommerceOptionsValidator.ValidCurrency(command.Currency) || command.Quantity != CommercePolicy.GameLicenseQuantity ||
            !CommerceOptionsValidator.Identifier(command.PriceReference, "price_") || !CommerceOptionsValidator.Identifier(command.CatalogProductReference, "prod_") ||
            command.ExpiresAt.Kind != DateTimeKind.Utc || command.ExpiresAt <= DateTime.UnixEpoch || command.ExpiresAt > clock.GetUtcNow().UtcDateTime.Add(CommercePolicy.CheckoutLifetime).AddMinutes(1) ||
            command.PaymentMethods.Count == 0 || command.PaymentMethods.Any(x => !CommerceOptionsValidator.Text(x, 32) || !x.All(c => char.IsAsciiLetterLower(c) || c == '_')) ||
            !ReturnUrl(command.SuccessUrl, command.OrderId) || command.CancelUrl != command.SuccessUrl + "?canceled=true")
            throw new PaymentProviderException("INVALID_CHECKOUT_COMMAND");
        await Merchant(ct);
        await Offer(command.PriceReference, command.CatalogProductReference, command.AmountMinor, command.Currency, true, ct);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["order_id"] = command.OrderId.ToString("D"), ["attempt_id"] = command.AttemptId.ToString("D") };
        var session = await client.V1.Checkout.Sessions.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment", UiMode = "hosted_page", LineItems = [new() { Price = command.PriceReference, Quantity = command.Quantity }],
            CustomerEmail = command.Email, SuccessUrl = command.SuccessUrl, CancelUrl = command.CancelUrl,
            ClientReferenceId = command.OrderId.ToString("D"), Metadata = metadata, PaymentIntentData = new() { Metadata = metadata },
            AutomaticTax = new() { Enabled = true }, AllowedPaymentMethodTypes = command.PaymentMethods.ToList(),
            AllowPromotionCodes = false, AdaptivePricing = new() { Enabled = false }, ManagedPayments = new() { Enabled = false }, ExpiresAt = command.ExpiresAt,
        }, new RequestOptions { IdempotencyKey = command.OperationKey }, ct);
        if (session.Livemode || !CommerceOptionsValidator.Identifier(session.Id, "cs_") || !SafeCheckoutUrl(session.Url) ||
            session.ExpiresAt != command.ExpiresAt) throw new PaymentProviderException("INVALID_CHECKOUT_RESULT");
        return new CheckoutProviderResult(session.Id, session.Url, session.ExpiresAt);
    }, ct);

    public Task<PaymentSnapshot> GetCheckoutAsync(PaymentLookup lookup, CancellationToken ct) => Safe(async () =>
    {
        Enabled();
        await Merchant(ct);
        Session session;
        if (lookup.CheckoutReference is { } checkout && CommerceOptionsValidator.Identifier(checkout, "cs_"))
            session = await client.V1.Checkout.Sessions.GetAsync(checkout, cancellationToken: ct);
        else if (lookup.PaymentReference is { } payment && CommerceOptionsValidator.Identifier(payment, "pi_"))
        {
            var sessions = new List<Session>();
            await foreach (var candidate in client.V1.Checkout.Sessions.ListAutoPagingAsync(new() { PaymentIntent = payment, Limit = 100 }, cancellationToken: ct))
            {
                sessions.Add(candidate);
                if (sessions.Count > 1) throw new PaymentProviderException("AMBIGUOUS_CHECKOUT");
            }
            session = sessions.SingleOrDefault() ?? throw new PaymentProviderException("CHECKOUT_NOT_FOUND");
        }
        else throw new PaymentProviderException("INVALID_PAYMENT_LOOKUP");
        if (session.Livemode || session.Mode != "payment" || !Guid.TryParse(session.Metadata.GetValueOrDefault("order_id"), out var order) || order == Guid.Empty ||
            !Guid.TryParse(session.Metadata.GetValueOrDefault("attempt_id"), out var attempt) || attempt == Guid.Empty ||
            !CommerceOptionsValidator.ValidCurrency(session.Currency) || session.AmountTotal is not > 0 || session.TotalDetails?.AmountDiscount != 0 || session.TotalDetails.AmountShipping != 0 ||
            lookup.PaymentReference is { } expected && session.PaymentIntentId != expected) throw new PaymentProviderException("INVALID_PAYMENT_BINDING");
        var lines = new List<LineItem>();
        await foreach (var line in client.V1.Checkout.Sessions.ListLineItemsAutoPagingAsync(session.Id, new() { Limit = 100 }, cancellationToken: ct))
        {
            lines.Add(line);
            if (lines.Count > 1) throw new PaymentProviderException("INVALID_PAYMENT_LINES");
        }
        var item = lines.SingleOrDefault() ?? throw new PaymentProviderException("INVALID_PAYMENT_LINES");
        if (item.Quantity != CommercePolicy.GameLicenseQuantity || item.Currency != session.Currency || item.AmountTotal != session.AmountTotal || item.Price is null)
            throw new PaymentProviderException("INVALID_PAYMENT_LINES");
        await Offer(item.Price.Id, item.Price.ProductId, session.AmountTotal.Value, session.Currency, false, ct);
        var refunds = new List<RefundProviderResult>();
        var disputes = new List<PaymentDisputeSnapshot>();
        if (session.PaymentIntentId is { } intent)
        {
            var payment = await client.V1.PaymentIntents.GetAsync(intent, cancellationToken: ct);
            if (payment.Livemode || payment.Id != intent || payment.Currency != session.Currency || payment.Amount != session.AmountTotal ||
                session.PaymentStatus == "paid" && (payment.Status != "succeeded" || payment.AmountReceived != session.AmountTotal))
                throw new PaymentProviderException("INVALID_PAYMENT_BINDING");
            await foreach (var refund in client.V1.Refunds.ListAutoPagingAsync(new() { PaymentIntent = intent, Limit = 100 }, cancellationToken: ct))
            {
                if (refund.PaymentIntentId != intent || refund.Currency != session.Currency) throw new PaymentProviderException("INVALID_REFUND_BINDING");
                refunds.Add(new(refund.Id, intent, refund.Amount, refund.Currency, StripePaymentMapping.Refund(refund.Status)));
            }
            await foreach (var dispute in client.V1.Disputes.ListAutoPagingAsync(new() { PaymentIntent = intent, Limit = 100 }, cancellationToken: ct))
            {
                if (dispute.Livemode || dispute.PaymentIntentId != intent || dispute.Currency != session.Currency) throw new PaymentProviderException("INVALID_DISPUTE_BINDING");
                disputes.Add(new(dispute.Id, intent, dispute.Amount, dispute.Currency, StripePaymentMapping.Dispute(dispute.Status)));
            }
        }
        var state = StripePaymentMapping.Checkout(session.Status, session.PaymentStatus);
        var taxComplete = session.AutomaticTax is { Enabled: true, Status: "complete" } && session.TotalDetails.AmountTax >= 0 && session.TotalDetails.AmountTax <= session.AmountTotal;
        if (state == PaymentAttemptState.Paid && (!taxComplete || string.IsNullOrEmpty(session.PaymentIntentId))) throw new PaymentProviderException("INCOMPLETE_PAYMENT_EVIDENCE");
        return new PaymentSnapshot(Provider, Configuration.ProviderAccountId, Configuration.PaymentEnvironment, order, attempt, session.Id, session.PaymentIntentId,
            item.Price.Id, item.Price.ProductId, checked((int)item.Quantity.Value), session.AmountTotal.Value, session.Currency, session.TotalDetails.AmountTax, session.AmountSubtotal, taxComplete,
            state == PaymentAttemptState.Paid, state, session.ExpiresAt, refunds, disputes);
    }, ct);

    public VerifiedPaymentNotification VerifyNotification(ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, string> headers, DateTime now)
    {
        try
        {
            Enabled();
            if (body.Length == 0 || body.Length > CommercePolicy.MaximumNotificationBytes || now.Kind != DateTimeKind.Utc) throw new PaymentProviderException("INVALID_NOTIFICATION");
            var signature = headers.SingleOrDefault(x => string.Equals(x.Key, "Stripe-Signature", StringComparison.OrdinalIgnoreCase)).Value;
            var notification = EventUtility.ConstructEvent(StrictUtf8.GetString(body.Span), signature, Configuration.WebhookSecret, CommercePolicy.NotificationClockSkewSeconds,
                new DateTimeOffset(now).ToUnixTimeSeconds());
            if (notification.ApiVersion != ApiVersion || notification.Livemode ||
                notification.Account is { } account && account != Configuration.ProviderAccountId) throw new PaymentProviderException("INVALID_NOTIFICATION_SCOPE");
            var type = notification.Type;
            var resource = notification.Data.Object;
            string kind, reference;
            string? payment = null;
            Guid? order = null, attempt = null;
            if (resource is Session session && type is "checkout.session.completed" or "checkout.session.async_payment_succeeded" or "checkout.session.async_payment_failed" or "checkout.session.expired")
            {
                kind = "checkout"; reference = session.Id; payment = session.PaymentIntentId;
                order = Guid.TryParse(session.Metadata?.GetValueOrDefault("order_id"), out var orderId) ? orderId : null;
                attempt = Guid.TryParse(session.Metadata?.GetValueOrDefault("attempt_id"), out var attemptId) ? attemptId : null;
            }
            else if (resource is Refund refund && type is "refund.created" or "refund.updated" or "refund.failed")
            { kind = "refund"; reference = refund.Id; payment = refund.PaymentIntentId; }
            else if (resource is Dispute dispute && type is "charge.dispute.created" or "charge.dispute.updated" or "charge.dispute.closed" or "charge.dispute.funds_withdrawn" or "charge.dispute.funds_reinstated")
            { kind = "dispute"; reference = dispute.Id; payment = dispute.PaymentIntentId; }
            else throw new PaymentProviderException("UNSUPPORTED_NOTIFICATION");
            if (!CommerceOptionsValidator.Text(reference, 256) || !CommerceOptionsValidator.Identifier(notification.Id, "evt_"))
                throw new PaymentProviderException("INVALID_NOTIFICATION");
            return new(Provider, Configuration.ProviderAccountId, Configuration.PaymentEnvironment, notification.Id, type, kind, reference, payment, order, attempt, notification.Created);
        }
        catch (PaymentProviderException) { throw; }
        catch (Exception) { throw new PaymentProviderException("INVALID_NOTIFICATION"); }
    }

    public Task<RefundProviderResult> RequestFullRefundAsync(FullRefundCommand command, CancellationToken ct) => Safe(async () =>
    {
        Enabled();
        if (!CommerceOptionsValidator.Text(command.OperationKey, 128) || !CommerceOptionsValidator.Identifier(command.PaymentReference, "pi_") ||
            command.AmountMinor <= 0 || !CommerceOptionsValidator.ValidCurrency(command.Currency)) throw new PaymentProviderException("INVALID_REFUND_COMMAND");
        await Merchant(ct);
        var payment = await client.V1.PaymentIntents.GetAsync(command.PaymentReference, cancellationToken: ct);
        if (payment.Livemode || payment.Currency != command.Currency || payment.AmountReceived != command.AmountMinor || payment.Status != "succeeded")
            throw new PaymentProviderException("INVALID_REFUND_BINDING");
        var refund = await client.V1.Refunds.CreateAsync(new() { PaymentIntent = command.PaymentReference, Amount = command.AmountMinor },
            new() { IdempotencyKey = command.OperationKey }, ct);
        if (refund.PaymentIntentId != command.PaymentReference || refund.Amount != command.AmountMinor || refund.Currency != command.Currency)
            throw new PaymentProviderException("INVALID_REFUND_RESULT");
        return new RefundProviderResult(refund.Id, refund.PaymentIntentId, refund.Amount, refund.Currency, StripePaymentMapping.Refund(refund.Status));
    }, ct);

    private void Enabled()
    {
        var permittedLicense = Configuration.LicenseEnvironment == CommerceEnvironments.DevelopmentLicense ||
            Configuration.AllowExistingAccountSandbox && Configuration.LicenseEnvironment == CommerceEnvironments.ProductionLicense;
        if (!Configuration.Enabled || Configuration.Provider != ProviderName || Configuration.PaymentEnvironment != CommerceEnvironments.Sandbox ||
            !permittedLicense || !Registration.SettingsAreValid(Configuration))
            throw new PaymentProviderException("PAYMENT_PROVIDER_DISABLED");
    }
    private async Task Merchant(CancellationToken ct)
    {
        // SDK 53's typed Accounts.Get takes a Connect account ID; this endpoint identifies the key's own merchant.
        var response = await client.RawRequestAsync(HttpMethod.Get, "/v1/account", null, null, ct);
        using var body = JsonDocument.Parse(response.Content);
        if (body.RootElement.GetProperty("id").GetString() != Configuration.ProviderAccountId) throw new PaymentProviderException("INVALID_MERCHANT");
    }
    private async Task Offer(string priceId, string productId, long amountMinor, string currency, bool requireActive, CancellationToken ct)
    {
        var price = await client.V1.Prices.GetAsync(priceId, cancellationToken: ct);
        var product = await client.V1.Products.GetAsync(productId, cancellationToken: ct);
        if (price.Livemode || product.Livemode || price.Id != priceId || price.ProductId != productId || product.Id != productId ||
            requireActive && (!price.Active || !product.Active) || price.Type != "one_time" || price.UnitAmount != amountMinor || price.Currency != currency || price.TaxBehavior != "inclusive")
            throw new PaymentProviderException("INVALID_PAYMENT_OFFER");
    }
    // Reservation owns the trusted origin. Replays retain that snapshot when deployment settings change.
    private static bool ReturnUrl(string value, Guid order) => Uri.TryCreate(value, UriKind.Absolute, out var url) &&
        url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 && url.Fragment.Length == 0 && url.Query.Length == 0 &&
        url.AbsolutePath == $"/account/purchases/{order:D}";
    public static bool SafeCheckoutUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme == "https" &&
        url.Host == "checkout.stripe.com" && url.IsDefaultPort && url.UserInfo.Length == 0;
    private static async Task<T> Safe<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try { return await action(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PaymentProviderException) { throw; }
        catch (Exception) { throw new PaymentProviderException("PAYMENT_PROVIDER_UNAVAILABLE"); }
    }
}

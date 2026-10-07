using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalon.Api.Commerce;
using Avalon.Domain.Commerce;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Stripe;
using Xunit;

namespace Avalon.Api.UnitTests.Commerce;

public sealed class StripePaymentProviderShould
{
    [Fact]
    public async Task Create_checkout_uses_the_hosted_page_mode_required_by_the_pinned_API()
    {
        var http = new Transport { RequireHostedPageMode = true };
        Assert.Equal("cs_test", (await Provider(http).CreateCheckoutAsync(Command(), default)).CheckoutReference);
    }
    internal static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    internal static readonly Guid Order = Guid.NewGuid();
    internal static readonly Guid Attempt = Guid.NewGuid();

    [Fact]
    public async Task Create_fixed_inclusive_euro_checkout_with_the_persisted_operation_key()
    {
        var http = new Transport();
        CheckoutProviderResult result = await Provider(http).CreateCheckoutAsync(Command(), default);
        Assert.Equal("cs_test", result.CheckoutReference);
        Assert.Equal("https://checkout.stripe.com/c/pay/test", result.CheckoutUrl);
        Assert.Equal("operation-test", http.Key);
        string form = Uri.UnescapeDataString(http.Body!);
        Assert.Contains("automatic_tax[enabled]=true", form);
        Assert.Contains("managed_payments[enabled]=false", form);
        Assert.Contains("adaptive_pricing[enabled]=false", form);
        Assert.Contains("allowed_payment_method_types[0]=card", form);
        Assert.Contains("allow_promotion_codes=false", form);
        Assert.Contains("line_items[0][quantity]=1", form);
    }

    [Theory]
    [InlineData("exclusive", "acct_test", false)]
    [InlineData("inclusive", "acct_other", false)]
    [InlineData("inclusive", "acct_test", true)]
    public async Task Wrong_offer_account_or_environment_never_creates_checkout(string tax, string account, bool live)
    {
        var http = new Transport { TaxBehavior = tax, Account = account, Live = live };
        await Assert.ThrowsAsync<PaymentProviderException>(() => Provider(http).CreateCheckoutAsync(Command(), default));
        Assert.Null(http.Body);
    }

    [Theory]
    [InlineData("https://attacker.test/pay")]
    [InlineData("https://checkout.stripe.com:444/pay")]
    [InlineData("https://user@checkout.stripe.com/pay")]
    public async Task Unsafe_returned_checkout_url_is_not_exposed(string url)
    {
        var http = new Transport { Url = url };
        PaymentProviderException error = await Assert.ThrowsAsync<PaymentProviderException>(() => Provider(http).CreateCheckoutAsync(Command(), default));
        Assert.DoesNotContain(url, error.ToString());
        Assert.DoesNotContain("sk_test_private", error.ToString());
    }

    [Fact]
    public async Task Timeout_can_replay_the_identical_request_and_key()
    {
        var http = new Transport { Timeout = true };
        StripePaymentProvider provider = Provider(http);
        await Assert.ThrowsAsync<PaymentProviderException>(() => provider.CreateCheckoutAsync(Command(), default));
        string? original = http.Body;
        http.Timeout = false;
        await provider.CreateCheckoutAsync(Command(), default);
        Assert.Equal(original, http.Body);
        Assert.Equal("operation-test", http.Key);
    }

    [Fact]
    public async Task Snapshot_reads_all_refund_and_dispute_pages_and_keeps_unpaid_completion_pending()
    {
        PaymentSnapshot snapshot = await Provider(new Transport()).GetCheckoutAsync(new("cs_test", null), default);
        Assert.Equal(800, snapshot.AmountMinor);
        Assert.Equal("eur", snapshot.Currency);
        Assert.Equal(1, snapshot.Quantity);
        Assert.Equal("sandbox", snapshot.PaymentEnvironment);
        Assert.False(snapshot.Paid);
        Assert.Equal(PaymentAttemptState.Processing, snapshot.State);
        Assert.Equal(2, snapshot.Refunds.Count);
        Assert.Equal(PaymentDisputeState.Open, Assert.Single(snapshot.Disputes).State);
    }

    [Fact]
    public async Task Missing_line_items_are_unavailable_evidence()
    {
        await Assert.ThrowsAsync<PaymentProviderException>(() => Provider(new Transport { MissingLines = true }).GetCheckoutAsync(new("cs_test", null), default));
    }

    [Fact]
    public async Task Replay_uses_frozen_return_origin_and_methods_after_configuration_changes()
    {
        CommerceConfiguration config = CommerceConfigurationShould.Valid();
        config.PublicSiteOrigin = "https://new.example.test";
        config.PaymentMethods = ["card", "multibanco"];
        var http = new Transport();
        var provider = new StripePaymentProvider(Options.Create(config), new StripeClient("sk_test_private",
            httpClient: new SystemNetHttpClient(new HttpClient(http), maxNetworkRetries: 0)), new FakeTimeProvider(new DateTimeOffset(Now)));
        Assert.Equal("cs_test", (await provider.CreateCheckoutAsync(Command(), default)).CheckoutReference);
        Assert.Contains("allowed_payment_method_types[0]=card", Uri.UnescapeDataString(http.Body!));
        Assert.DoesNotContain("multibanco", http.Body);
    }

    [Fact]
    public async Task Idempotent_recovery_can_find_the_original_checkout_after_its_expiry()
    {
        var http = new Transport();
        var provider = new StripePaymentProvider(Options.Create(CommerceConfigurationShould.Valid()), new StripeClient("sk_test_private",
            httpClient: new SystemNetHttpClient(new HttpClient(http), maxNetworkRetries: 0)), new FakeTimeProvider(new DateTimeOffset(Now.AddHours(1))));
        Assert.Equal("cs_test", (await provider.CreateCheckoutAsync(Command(), default)).CheckoutReference);
        Assert.Equal("operation-test", http.Key);
    }

    [Fact]
    public async Task Full_refund_uses_stable_operation_and_does_not_claim_pending_is_success()
    {
        var http = new Transport { Paid = true };
        RefundProviderResult result = await Provider(http).RequestFullRefundAsync(new("refund-test", "pi_test", 800, "eur"), default);
        Assert.Equal(PaymentRefundState.Pending, result.State);
        Assert.Equal(800, result.AmountMinor);
        Assert.Equal("pi_test", result.PaymentReference);
        Assert.Equal("refund-test", http.Key);
        Assert.Contains("amount=800", http.Body);
    }

    [Fact]
    public async Task Configured_offer_and_saved_payment_drive_amounts_instead_of_literal_eight_euros()
    {
        CommerceConfiguration config = CommerceConfigurationShould.Valid();
        config.AmountMinor = 1200;
        config.Currency = "usd";
        var http = new Transport { Paid = true, Gross = 1200, Currency = "usd" };
        var provider = new StripePaymentProvider(Options.Create(config), new StripeClient("sk_test_private",
            httpClient: new SystemNetHttpClient(new HttpClient(http), maxNetworkRetries: 0)), new FakeTimeProvider(new DateTimeOffset(Now)));
        await provider.CreateCheckoutAsync(Command() with { AmountMinor = 1200, Currency = "usd" }, default);
        config.AmountMinor = 800;
        config.Currency = "eur";
        PaymentSnapshot snapshot = await provider.GetCheckoutAsync(new("cs_test", null), default);
        Assert.Equal(1200, snapshot.AmountMinor);
        Assert.Equal("usd", snapshot.Currency);
        RefundProviderResult refund = await provider.RequestFullRefundAsync(new("refund-test", "pi_test", 1200, "usd"), default);
        Assert.Equal(1200, refund.AmountMinor);
        Assert.Contains("amount=1200", http.Body);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(-301, false, true)]
    [InlineData(0, true, true)]
    public void Verify_raw_signature_and_environment_before_normalizing(int age, bool live, bool rejected)
    {
        long timestamp = new DateTimeOffset(Now.AddSeconds(age)).ToUnixTimeSeconds();
        string raw = JsonSerializer.Serialize(new
        {
            id = "evt_test",
            @object = "event",
            api_version = "2026-09-30.endive",
            created = timestamp,
            livemode = live,
            type = "checkout.session.completed",
            data = new { @object = new { id = "cs_test", @object = "checkout.session", livemode = live } }
        });
        string signed = $"{timestamp}.{raw}";
        string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_private"), Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
        var headers = new Dictionary<string, string> { ["Stripe-Signature"] = $"t={timestamp},v1={signature}" };
        if (rejected) Assert.Throws<PaymentProviderException>(() => Provider(new Transport()).VerifyNotification(Encoding.UTF8.GetBytes(raw), headers, Now));
        else Assert.Equal("cs_test", Provider(new Transport()).VerifyNotification(Encoding.UTF8.GetBytes(raw), headers, Now).ResourceReference);
        headers["Stripe-Signature"] = $"t={timestamp},v1={new string('0', 64)}";
        Assert.Throws<PaymentProviderException>(() => Provider(new Transport()).VerifyNotification(Encoding.UTF8.GetBytes(raw), headers, Now));
    }

    internal static CheckoutCreateCommand Command() => new(Order, Attempt, "operation-test", "price_test", "prod_test", 800, "eur", 1,
        "player@example.test", $"https://example.test/account/purchases/{Order:D}", $"https://example.test/account/purchases/{Order:D}?canceled=true", Now.AddMinutes(30), ["card"]);
    internal static StripePaymentProvider Provider(Transport http) => new(Options.Create(CommerceConfigurationShould.Valid()),
        new StripeClient("sk_test_private", httpClient: new SystemNetHttpClient(new HttpClient(http), maxNetworkRetries: 0)), new FakeTimeProvider(new DateTimeOffset(Now)));

    internal sealed class Transport : HttpMessageHandler
    {
        public bool RequireHostedPageMode { get; init; }
        public string TaxBehavior { get; init; } = "inclusive";
        public string Account { get; init; } = "acct_test";
        public bool Live { get; init; }
        public string Url { get; init; } = "https://checkout.stripe.com/c/pay/test";
        public bool Timeout { get; set; }
        public bool MissingLines { get; init; }
        public bool Paid { get; init; }
        public bool Refunded { get; init; }
        public bool Disputed { get; init; } = true;
        public long Gross { get; init; } = 800;
        public string Currency { get; init; } = "eur";
        public string? Body { get; private set; }
        public string? Key { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            object payload;
            if (path == "/v1/account")
            {
                payload = new { id = Account, @object = "account" };
            }
            else if (path.StartsWith("/v1/prices/", StringComparison.Ordinal))
            {
                payload = new
            {
                id = "price_test",
                @object = "price",
                active = true,
                livemode = Live,
                type = "one_time",
                currency = Currency,
                unit_amount = Gross,
                tax_behavior = TaxBehavior,
                product = "prod_test"
            };
            }
            else if (path.StartsWith("/v1/products/", StringComparison.Ordinal))
            {
                payload = new { id = "prod_test", @object = "product", active = true, livemode = Live };
            }
            else if (path.StartsWith("/v1/payment_intents/", StringComparison.Ordinal))
            {
                payload = new
            {
                id = "pi_test",
                @object = "payment_intent",
                livemode = false,
                amount = Gross,
                amount_received = Paid ? Gross : 0,
                currency = Currency,
                status = Paid ? "succeeded" : "processing"
            };
            }
            else if (path.EndsWith("/line_items", StringComparison.Ordinal))
            {
                payload = new
            {
                @object = "list",
                has_more = false,
                data = MissingLines ? Array.Empty<object>() : new object[] {
                new { id = "li_test", @object = "item", quantity = 1, amount_total = Gross, currency = Currency, price = new { id = "price_test", @object = "price", product = "prod_test" } } }
            };
            }
            else if (path == "/v1/refunds" && request.Method == HttpMethod.Post)
            {
                Body = await request.Content!.ReadAsStringAsync(ct);
                Key = request.Headers.GetValues("Idempotency-Key").Single();
                payload = new { id = "re_test", @object = "refund", status = "pending", amount = Gross, currency = Currency, payment_intent = "pi_test" };
            }
            else if (path == "/v1/refunds")
            {
                payload = new
            {
                @object = "list",
                has_more = !request.RequestUri.Query.Contains("starting_after", StringComparison.Ordinal),
                data = new[] {
                new { id = request.RequestUri.Query.Contains("starting_after", StringComparison.Ordinal) ? "re_two" : "re_one", @object = "refund", status = Refunded ? "succeeded" : "failed", amount = Gross, currency = Currency, payment_intent = "pi_test", livemode = false } }
            };
            }
            else if (path == "/v1/disputes")
            {
                payload = new { @object = "list", has_more = false, data = Disputed ? new object[] { new { id = "dp_test", @object = "dispute", status = "needs_response", amount = Gross, currency = Currency, payment_intent = "pi_test", livemode = false } } : [] };
            }
            else
            {
                if (request.Method == HttpMethod.Post)
                {
                    Body = await request.Content!.ReadAsStringAsync(ct);
                    Key = request.Headers.GetValues("Idempotency-Key").Single();
                    if (Timeout) throw new TaskCanceledException("private transport details");
                    if (RequireHostedPageMode && !Uri.UnescapeDataString(Body).Split('&').Contains("ui_mode=hosted_page", StringComparer.Ordinal))
                        return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"message\":\"Use hosted_page for this API version.\",\"param\":\"ui_mode\",\"type\":\"invalid_request_error\"}}", Encoding.UTF8, "application/json") };
                }
                payload = new
                {
                    id = "cs_test",
                    @object = "checkout.session",
                    livemode = false,
                    url = Url,
                    mode = "payment",
                    status = "complete",
                    payment_status = Paid ? "paid" : "unpaid",
                    payment_intent = "pi_test",
                    expires_at = new DateTimeOffset(Now.AddMinutes(30)).ToUnixTimeSeconds(),
                    amount_total = Gross,
                    amount_subtotal = Gross,
                    currency = Currency,
                    automatic_tax = new { enabled = true, status = "complete" },
                    total_details = new { amount_tax = 0, amount_discount = 0, amount_shipping = 0 },
                    metadata = new { order_id = Order.ToString("D"), attempt_id = Attempt.ToString("D") }
                };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        }
    }
}

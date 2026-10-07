# Website purchases

Website checkout defaults to disabled. Existing Steam ownership and native launcher handoff remain independent of checkout. Epic is deferred.

The configured offer is one perpetual Avalon license, initially EUR 800 minor units (€8 including applicable tax), with card payments. Amount, currency, merchant, catalog/price IDs, payment methods and public return origin are server configuration. Each order freezes its price/currency; each attempt freezes provider scope, price/product, methods, return URLs and expiry. Changing configuration never rewrites historical orders. A different store/payment service supplies the common provider interface and shared purchase/license tables.

## The commerce service

Checkout, purchase and license status, the admin purchase tools, payment notifications and the reconciliation worker are the REST API's commerce service, `Avalon.Api.Commerce` ([API services](api-services.md)): it owns `/account/game-license`, `/account/purchases`, `/admin/purchases` and `/payments/notifications`, and a process runs it when `Application:Services` names it or is unset. It needs Redis (the checkout budget, `commerce:{environment}:checkout:*`) and the auth database, whose purchase, payment and license tables it reads and writes and whose schema identity migrates (a process without identity waits for that at startup). It reads `Application:StoreAuthentication:Environment` and `SteamIdentityPrefix`, which scope purchases and licenses, and binds that section without identity's Steam checks; it needs no Steam publisher key. Production still runs commerce inside the one `avalon-api` process until the rollout (#802), whose plan moves it last.

## Deployment

Use `Application:Commerce` or the chart's `commerce` values, which the chart renders only into a release that runs commerce. Enabled commerce takes sandbox payments only, in one of two setups, and refuses to start otherwise (`CommerceOptionsValidator`, mirrored by the chart):

- an isolated Development sandbox: a Development host, `Application:StoreAuthentication:Environment=development`, `SteamIdentityPrefix=avalon-auth-dev` and development licenses (`LicenseEnvironment=development`);
- an explicit existing-account sandbox: `AllowExistingAccountSandbox=true` on a Production host whose `Application:StoreAuthentication:Environment` and `LicenseEnvironment` are `production`, so sandbox payments grant licenses to existing production accounts. Payment credentials and provider evidence must still be test-mode only.

Live payments are refused. Deploy an isolated hosted sandbox only with separate namespace, PostgreSQL Auth/world/character storage, Redis, signing/workload credentials and ingress; on the existing live or PTR deployment, checkout can be on only through the explicit existing-account opt-in.

Configure `Provider`, `ProviderAccountId`, `OfferId`, `ProviderPriceId`, `ProviderCatalogProductId`, `AmountMinor`, `Currency`, `Quantity=1`, `Product=avalon.base`, `ProviderProduct=base`, `PublicSiteOrigin` (bare HTTPS origin), and `PaymentMethods`. The Stripe adapter verifies the merchant, active one-time inclusive price/product and exact amount/currency before creating Checkout. A Checkout Session uses automatic tax, explicit payment methods, no adaptive pricing, no managed payments, no promotion codes, and a durable operation key. Verify the appropriate **sandbox-only** tax settings with Stripe; do not copy a test tax registration to live configuration.

Keys are Secret references only: `commerce.existingSecret`, `apiKeyKey` (default `stripe-api-key`) and `webhookSecretKey` (default `stripe-webhook-secret`). Homelab Secret manifests use SOPS, following that repository's existing recipient/key convention. Never place credentials in Helm values, ConfigMaps, command-line arguments, pull requests or chat. This feature adds no production Secret or deployment.

Stripe.net 53.0.0 uses API version `2026-09-30.endive`. Configure the sandbox notification destination to that version. The destination is `POST /payments/notifications/stripe`; a proxy may prefix it with `/api`. The signing secret belongs to that exact sandbox destination (Stripe CLI forwarding has its own secret). Enable Checkout Session completed/expired/asynchronous payment success/failure, refund created/updated/failed, and charge dispute created/updated/closed events. Signed events are persisted before acknowledgement; provider state is retrieved independently before fulfillment. Never log raw notification bodies, signature headers, keys, card data or Checkout URLs. Enabled commerce suppresses EF sensitive-data logging for every context of the process it runs in, even on a Development host.

## Recovery and support

An open Checkout can be resumed from the account's license card. Recovery reuses its saved operation, URL and expiry. The admin purchase page offers Resume refund request for an unknown outbound result; it retains the original amount/reason/key and refuses replay after its deadline. A known pending provider refund stays blocked while reconciliation checks it. Terminal failed/canceled refunds permit a new full-refund request only after their outcome is verified. If a successful refund is later reported otherwise, revocation remains terminal and the contradictory observation is flagged for manual review.

The admin purchase page shows purchaser/beneficiary, original total/tax, payment attempts, funding license, refunds/disputes and processing history. Refund requests contain only a recorded attempt ID and reason. Only full refunds are offered; pending/failed/canceled results do not revoke access. A confirmed full refund of the funding payment revokes its license; refunding a duplicate payment does not revoke the original grant. Open formal disputes suspend access; a win releases that dispute's hold, requiring fresh authentication; loss/acceptance is terminal. Other holds cannot be cleared by a dispute result.

The inbox worker and periodic sweep recover missed notifications. Unknown Checkout and refund operations retain their original idempotency key and request. Replay is bounded to 23 hours, shorter than Stripe's documented minimum retention; after that, support review is required. Retry reconciliation schedules trusted reads/recovery; it cannot force success, change an amount, mint another operation key or revive a revoked license. Refund observations without a known operation must not be guessed to belong to a particular administrator request.

## Release checks

Before release verify real sandbox paid/declined/pending/abandoned payments, duplicate/reordered notifications and restart recovery, full refund, dispute open/win/loss, exactly one development grant, fresh native handoff and revocation disconnect. Confirm that development grants cannot enter production or live PTR. Review and merge are performed by the human; the agent never merges PRs.

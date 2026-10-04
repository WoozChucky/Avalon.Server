# Steam account linking and consolidation

The game creates a store-generated Avalon account and its Steam link atomically on the first verified Steam launch. Store-generated accounts have no invented email address or password. Game access independently requires a current ownership check for application 2499460.

Website linking keeps the currently authenticated Avalon account. When Steam already belongs to an eligible store-generated account, all of that account's characters move to the website account. Character IDs, inventory, quests and other child records stay attached to the same characters. The website account keeps its credentials and permissions; the automatic source account is deactivated. Existing characters are retained even when their combined count exceeds the creation limit.

## Browser verification

The player starts from **Account → Steam → Link Steam account**. Steam verifies its identity through the pinned `AspNet.Security.OpenId.Steam` provider. The callback never signs a Steam principal into Avalon or grants a game license. Confirmation requires the website account's current password, its current MFA code when enrolled, and an explicit confirmation of the transfer.

Transactions bind the target account, credentials version, session epoch, original website JWT session and a Secure/HttpOnly browser cookie. Provider state is authenticated and encrypted. Steam identifiers and provider endpoints must be canonical; signed nonces must be fresh and can be consumed once across transactions. Provider replies are bounded to 16 KiB and five seconds. Discovery, profile lookup and redirects are disabled. Callback query strings, provider payload logs and callback/provider traces are suppressed.

Set these runtime settings before starting the API:

```text
Application__SteamWebLink__CallbackUrl=https://avalon.example/api/account/links/steam/callback
Application__SteamWebLink__SiteUrl=https://avalon.example
```

Both URLs must use HTTPS and contain no query, fragment or user information. CallbackUrl must end in `/account/links/steam/callback`. Set the real external URL, including any proxy prefix: request Host or forwarded headers never choose the return URL or Steam realm. Only explicitly configured proxies may establish HTTPS and the caller's address. Keep the site's API origin within the configured CORS allowlist; credentialed browser requests are required for the transaction cookie.

For local testing, the existing API HTTPS launch profile uses `https://localhost:7166`. Serve the public site through trusted HTTPS and set SiteUrl to that address. Use the configured HTTPS API callback directly or the site's HTTPS `/api` proxy. A local HTTP site cannot exercise Secure cookie linking. No Steam API publisher key is used for browser profile lookup; the separate game proof/ownership clients require their existing private publisher key.

## Durable transfer and recovery

Authorization commits one durable operation in the Auth database and freezes gameplay for both roots. Source game families and target launcher families are revoked, and both session epochs advance. The target website session remains available to manage and resume the operation.

Every configured world, including historical worlds, belongs to the persisted manifest. Missing configuration or an unavailable world prevents starting or completing a transfer. Each Character database first places both account guards into drain mode. Existing admitted writers may finish saving until end acknowledgment or their original lease expires; new admission and ordinary character mutations are blocked. Final saves retain their original immutable authority. A world then blocks both guards, changes each source character's account ID in one local transaction, and commits a durable transfer receipt.

After every world has committed, the Auth database moves the Steam identity and deactivates the source. Only then are target guards released. The source guards remain blocked. Admission still needs a new valid game context, ownership verification and a new session fence.

There is no distributed transaction. A lost Auth progress acknowledgment recovers from the world's receipt. Outages preserve completed progress and leave the operation resumable. The account page discovers unfinished operations, and the player can reopen **Resume character transfer** after closing the browser. Durable resumption requires authentication as the target account; it does not require reusing an expired Steam assertion or repeating MFA.

Use `GET /account/links/steam/consolidations/{id}` for owned progress and `POST /account/links/steam/consolidations/{id}/resume` to retry. These endpoints accept no caller-selected source account or world manifest. Original confirmation retries retain the same Idempotency-Key; new confirmation IDs cannot consume an already committed proof.

Only store-generated, active, unlocked source accounts with ordinary player permissions and a single Steam identity are eligible. A ban, privilege, conflicting provider identity or manually created source account is never removed by consolidation. Recovery credentials and permissions are not copied from the source.

## Coordinated rollout

Apply Auth and Character migrations to every durable world. Ship the matching world session/drain enforcement, API, native client and launcher together using the authentication plan's protocol cutover. Save fencing and consolidation are not a standalone legacy world admission path. The remaining native admission and lifecycle tasks must pass before deployment.

Verification covers three separate PostgreSQL databases, durable retries after injected outages/lost acknowledgments, final-save preservation, immutable character IDs/children, stale writer rejection, real Redis proof/consent races, fixed browser return URLs, maintained OpenID callback handling, and callback logging/tracing suppression.

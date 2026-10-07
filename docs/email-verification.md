# Email verification

How an account proves it owns its current email address. The REST API's identity service runs it
(`AccountEmailVerificationController`, `AccountEmailVerificationService`, `AccountEmailVerificationRepository`); the
settings are under [REST API Email](configuration-reference.md#rest-api-email).

## What it is for

`Accounts.EmailVerifiedAt` records when the address was proved, and `AccountDto.EmailVerifiedAt` shows it. A purchase
needs it: reserving a purchase for an account without a verified address is refused, 403 `EMAIL_NOT_VERIFIED`
(`PurchaseRepository.ReserveAsync`), and the purchase status reports whether the address is verified. Confirming an
email change proves the new address too, so `AccountService.ConfirmEmailChangeAsync` writes it with the address
(`AccountRepository.SetConfirmedEmailAsync`).

## Endpoints

All three are `/account/email/verification` and need a signed-in account (Player policy), acting on its own address
only.

| Request | Answer |
|---|---|
| `GET /account/email/verification` | `AccountEmailVerificationStatusDto`: `emailVerifiedAt`, `deliveryAvailable` (a sender and a site origin are configured), `resendAvailableAt` (set while the cooldown runs) |
| `POST /account/email/verification` | 202 once a link is sent, or at once when the address is already verified |
| `POST /account/email/verification/confirm` with `{ "token": "..." }` | 204 once the address is verified |

## Requesting a link

A caller with no peer address is refused with 400 before anything is read (`BaseController.SourceAddress`). Then
`RequestAsync` checks, in this order:

1. Delivery is available: an email sender (`Application:Email:Sender` other than `None`) and
   `Application:Email:VerificationSiteOrigin`. Otherwise 501 (`EmailVerificationUnavailableException`).
2. The account has a valid email, is Active, holds the Player flag, is not locked and has not been consolidated into
   another account. Otherwise 400 "A current account email is required for verification.".
3. An address already verified ends the request here, answered 202 with nothing sent.
4. One slot of each hourly send budget, in Redis: `email-verification:account:{accountId}`
   (`MaxVerificationSendsPerAccount`, default 5) and `email-verification:source:{source}`
   (`MaxVerificationSendsPerSource`, default 20, the source as the login budgets reduce it: an IPv4 address or an IPv6
   /64). Past either, 429 `LOCKED`. A slot is never given back, whatever happens next, so retries cannot multiply
   the emails sent.
5. The challenge is written in one Auth-database transaction that first takes the account's row lock: one row per
   account in `AccountEmailVerifications`, holding the SHA-256 of the token (never the token), the address, the
   account's credentials version and when it was issued, expires (30 minutes) and was consumed or invalidated. It is
   refused if the account changed since it was read (another address, a credentials change, no longer eligible:
   400 "Account changed. Try again."), or if the last link was issued less than `VerificationCooldownSeconds`
   (default 60) ago (429 `LOCKED`). A new link replaces the account's earlier one, so only the latest works.
6. The email goes to the current address, plain text, with its own 30-second timeout rather than the request's:
   `{VerificationSiteOrigin}/account/email/verify#token={token}`. The token is 32 random bytes in unpadded base64url
   (43 characters), in the link's fragment, so it never reaches a server log. A send that fails invalidates the
   challenge it wrote and answers 503 "Email could not be sent" (`EmailDeliveryException`); failures are logged
   with the account and the exception type only.

## Confirming

The website opens the link, reads the token from the fragment and posts it to
`/account/email/verification/confirm` as the signed-in account. The request validates the token's shape (43 base64url
characters). `ConfirmAsync` then, in one transaction under the same row lock, accepts it only if it is the account's
current challenge (by digest), neither consumed nor invalidated nor expired, the account is still eligible and not
yet verified, and its address and credentials version are still the ones the challenge was issued for. It then sets
`EmailVerifiedAt` and marks the challenge consumed. Any other token is 400 "This verification link is invalid or
expired. Request a new email.", whichever check refused it.

## Tests

`AccountEmailVerificationServiceShould` and `AccountEmailVerificationControllerShould` (`Avalon.Api.Identity.UnitTests`),
and `AccountEmailVerificationShould` (`Avalon.Database.UnitTests`) for the repository's transactions.

# Steam authentication and controlled rollout

The Steam App ID is required deployment configuration (`Application:StoreAuthentication:SteamAppId`), supplied by homelab. There is no runtime App ID default in the .NET stack. Steam is integrated in the game runtime; the engine has no Steam account or licensing dependency. A Steam launch uses `--channel steam`; the Avalon launcher uses `--channel avalon` and passes its short-lived account handoff through child stdin. Epic is explicitly unsupported in this build.

## Account behavior

A verified first Steam launch with a valid license atomically creates an Avalon account and its Steam link when the Steam identity is unknown. Generated accounts have no fabricated email or password. Recovery credentials are a separate account flow.

Website linking requires a signed-in Avalon account, current password/MFA, fresh Steam browser proof and explicit consent. If Steam already belongs to an automatic account, the website account survives: all characters move across every world without changing CharacterIds or child records, then the Steam link moves and the source account retires. The durable consolidation drains gameplay/saves, freezes both roots and resumes after failures. It never partially transfers access while a world is unavailable.

The launcher handoff selects its Avalon root; it does not establish Steam ownership and cannot silently switch to the Steam client's account. The game displays pending-license and Play-through-Steam actions. Takeover requires explicit confirmation.

## Authority and revocation

Game contexts, join tickets and SQL session fences have separate authority. Worlds require TLS, the trusted server name and exact leaf certificate SHA-256, and authenticated workload redemption. Legacy game-ticket login/world keys cannot admit gameplay. The supported world protocol starts at **0.2.0**.

The native runtime renews with fresh Steam Web API tickets and matching callback handles. Positive ownership grants at most five minutes; identity proof lasts at most thirty minutes. Timeout/429/5xx keep the original deadline. A definitive negative result persists a negative observation and invalidates every older dependent context; a later positive record cannot resurrect those contexts. Steam links are checked again at context/heartbeat admission.

Web API ticket identities use a 28-character lowercase format: an environment/product prefix (`pm`, `pt`, `dm`, or `dt`) followed by a 128-bit random nonce encoded as 26 unpadded base32 characters. The exact App ID remains bound in the stored attempt and the Steam verification request. The format fits the native generic-string limit and omits colons to avoid ambiguity with Steam's typed identity parsing. Live native verification rejected the old 57-character identity and a short `pt:` identity with error 105 (`Identity parameter did not match`), while the new plain 28-character format verified successfully. The shipped client's character filter already accepts this format.

Logout and refresh reuse revoke the durable context. Redis notices request an authoritative heartbeat for the affected context or account; a delayed notice cannot disconnect a different context. Lost notices remain covered by heartbeat and lease deadlines. Native cancellation sends best-effort HTTPS logout and wipes local secrets; shutdown allows at most two seconds for it. World HTTP work and persistence queues are bounded. A queue refusal never grants authority.

Account recovery, bans/deactivation, role changes and consolidation remain enforced by credential version, session epoch and current account state. World heartbeats run every fifteen seconds; leases last at most forty-five seconds and stop gameplay before expiry to allow draining. Character writes validate the immutable account/session/fence and current owner inside their database transaction.

## Private deployment configuration

Store authentication, Steam sign-in, account links and game admission are the REST API's identity service (`Avalon.Api.Identity`, [API services](api-services.md)). The publisher key, the store settings and the workload certificates below go only to a process, or a chart release, that runs identity; commerce reads only `Application:StoreAuthentication:Environment` and `SteamIdentityPrefix`, and no other service reads the section. In production identity runs in the `avalon-api` release (#802).

Supply `Application:StoreAuthentication:SteamPublisherKey` through the backend's private secret provider (`Application__StoreAuthentication__SteamPublisherKey` for environment injection). Do not put its plaintext in appsettings, source control, chat, client packages or CI logs. Homelab stores only a SOPS-encrypted Secret using its existing age recipient. Set `Application:StoreAuthentication:SteamAppId` explicitly and keep it consistent with the game/depot identity and environment/product settings. The API chart requires `storeAuthentication.steamAppId` in a release that runs identity; homelab owns its actual value. The supplied publisher key is configured in the encrypted homelab Secret; controlled Steam accounts and actual publisher authorization still require live provider verification.

Configure the workload client certificate, world TLS private key and API certificate bindings described in [game server admission configuration](steam-authentication-workloads.md). Required certificate secrets are mounted read-only. Client `netconfig.json` supplies the trusted HTTPS API and website locations; a local override is excluded from packages. Steam depots use `--channel steam`, contain the Steam runtime DLL and exclude `steam_appid.txt`. Avalon packages use `--channel avalon` and omit the Steam DLL; the runtime delay-loads it only on the Steam route.

## User smoke checks (not yet performed)

Use a controlled environment with the publisher key, workload certificates, matching 0.2.0 services/client and a real Steam license.

- Launch through Steam with an existing linked account; verify the expected Avalon root, world/character access and overlay.
- Launch with an unknown licensed Steam account; verify exactly one automatically linked root and normal character creation.
- Link Steam from an existing website account; authenticate password/MFA, review transfer consent, and verify every world's characters and unchanged CharacterIds after consolidation. Repeat/resume after a world outage.
- Launch from Avalon without a current license proof; verify pending-license and the explicit Steam action, without changing the Avalon root.
- Use a Steam identity linked to a different Avalon root; verify the actionable mismatch and absence of silent account switching.
- Reconnect and attempt a second concurrent session; verify explicit takeover, old-session disconnection and save fencing.
- Test cancellation/logout during admission, ownership loss, removed link, recovery, ban/deactivation and dropped Redis notices; verify refusal or bounded lease expiry without stale writes.
- Verify token redaction and successful certificate/name checks in the controlled environment.

These are external gates. Unit checks, disposable database/Redis acceptance tests and SDK initialization do not establish live provider verification.

## Coordinated cutover and rollback

Stage the API/database migrations, world services/schema, client, launcher and dashboard together in the controlled environment. Configure private secrets before startup. Keep the schema/vendor manifest synchronized and require client protocol 0.2.0 at the world boundary. Run the smoke checks before production rollout; no deployment or PR is implied by this implementation.

Rollback must retain TLS, current ownership checks, short-lived admission and SQL fencing. Do not roll back to a client/world combination that accepts legacy game tickets or world keys. If a compatible enforced build is unavailable, leave admissions closed while correcting the release. Do not undo completed account consolidations or reset fencing tokens.


## Automated verification record — 2026-10-04

Implementation, automated checks and the encrypted deployment-secret setup are complete on feature branches.
Real Steam smoke checks and coordinated staging remain pending; no live provider verification is claimed.

| Suite | Passed | Existing skips |
|---|---:|---:|
| API | 1529 | 0 |
| Database | 283 | 0 |
| World | 3716 | 1 |
| Auth | 394 | 0 |
| Shared | 1611 | 1 |
| Combat | 192 | 0 |
| Balance | 238 | 0 |
| Balance Service | 93 | 0 |
| Launcher core | 89 | 0 |
| Launcher desktop | 66 | 0 |
| Dashboard account/Steam linking (focused) | 8 | 0 |
| Python distribution | 44 | 1 existing case deselected |
| Native wire codec | 1 | 0 |

Runtime/editor builds passed, including a fresh-object build using a fresh pinned Steam SDK fetch.
Ignored native checks exercised route selection, asynchronous HTTP/cancellation, logout with the current
rotated credential, terminal revocation, exact certificate pin/name/expiry checks and admission root binding.
Dashboard typechecks and launcher builds passed. The packed client data was recooked.

Disposable PostgreSQL and isolated Redis checks covered one-use handoffs/proofs, same-identity linking,
automatic creation, 256 competing join-redemption connections, exact reservation retries, lost Redis receipts
after SQL commit, refresh reuse, lease renewal, takeover/save races, stale end operations, migration
rollback/reapply and model drift. Negative observation history cannot resurrect an older context;
a fresh positive proof can authorize a new one, and removed links refuse unused-ticket admission.
Character tests covered creation caps/atomic children, case-insensitive names, rollback after child failures,
consolidation ownership and stable CharacterIds, and writes waiting beyond expiry under row locks.
The expiry fixture waits for PostgreSQL's own deadline while holding the lock, rather than assuming a
host-side sleep crosses a database deadline. Temporary databases and isolated test keys were removed.

The cross-repository security review applied `.claude/agents/security-reviewer.md` from Avalon.Server:
JWT/PAT boundaries, Redis CAS/TTL, CSPRNG and MFA, one-use admission, input bounds, secret handling,
TLS/workload identity, revocation/fencing and authentication bypasses. It found one HIGH protocol mismatch
(allocator expected `1`, client sent `0.2.0`); the fix now uses the server protocol definition and rejects
unsupported attempts explicitly, with regression coverage and real allocator admission checks.
A follow-up reviewed constant substitutions, launcher/browser contracts and homelab/chart integration.
Its one LOW runbook encoding issue was fixed by restoring the original UTF-8 content before appending.
No remaining findings were reported. This is a code review and automated test record, not a penetration test.

Authority deadlines/input bounds are named in `Avalon.Common.GameAuth.GameAuthPolicy`; serialized
errors, states, channels and providers use named vocabulary. Native policy and JSON fields are defined
in `GameAuthPolicy.h`/`GameAuthContract.h`, with game error/state vocabulary in `GameAuthVocabulary.h`.
Native requests reuse `gamenet::kClientVersion`; Steam launch URLs reuse the game App ID definition.
Provider verification destinations remain fixed named protocol definitions. Deployment origins, assignments,
certificate references and publisher-key references are configuration; homelab prepares the actual environment
values. Shared wire values and client/server versions were checked for agreement.

All four API/world charts rendered from the actual homelab HelmRelease values. Negative render checks
refused missing publisher Secret references or workload assignments. Existing homelab release versions were
retained. The authorized follow-up configured the supplied publisher key and seven independent private
certificates/PFX passwords plus matching DER-leaf pins in six SOPS-encrypted homelab Secrets. Each
encrypted file passed MAC and exact value verification after decryption in memory; SAN, EKU, validity,
RSA key independence and matching pins were checked without logging plaintext. No machine trust roots,
certificate stores, Windows users or live workloads were changed.

World-to-internal-API trust now requires an exact API leaf SHA-256 pin. TLS verifies the API hostname,
validity, server-auth EKU and digital-signature usage; only chain errors for the pinned leaf are accepted.
The mandatory-pin refusal test failed before implementation. Real mTLS tests cover correct private leaves,
wrong pin, wrong name, expiry and pooled-connection expiry. Review identified a LOW synchronous-send
gap; its regression failed with no exception before the fix, and the same lifetime guard now applies to
both synchronous and asynchronous sends. The full World suite then passed 3716 tests with one existing
skip. All four actual HelmRelease value documents rendered; six missing-pin Secret/key cases refused
rendering. No live Steam/provider verification or release deployment is claimed.


### Deployment-owned Steam application identity

The approved follow-up removed the hardcoded App ID default and equality allowlist from shared
configuration. A missing/zero ID refuses startup with the named configuration field; the API Helm
chart also refuses a missing/zero value. Application IDs are trusted deployment input. An optional
attempt selector is resolved against this configuration; proofs cannot change the bound identity.
API DI passes the selected identity to verification, ownership requests,
and both license-observation persistence paths. World/auth services consume the resulting admission
authority and do not select a Steam application or make independent Steam verification calls.

Three regression cases failed before the change: a built-in default was present and alternate configured
positive IDs were rejected. The full API suite then passed 1529 tests. Mocked provider and DI tests use
explicit alternate IDs and assert their exact provider query values. Actual homelab Helm rendering
confirmed the configured ID reaches the API environment, an alternate value is respected, and missing/zero
values fail. A runtime/config/chart source scan found no game App ID literal or former constant.
Helm initially formatted the large numeric ID in scientific notation; the corrected chart validates uint32 range and integral values and emits canonical decimal. Six missing/invalid inputs refuse rendering, and alternate/maximum uint32 values render correctly. Security review confirmed configured authority remains separate from client input, with no findings.


### All Avalon test projects after the App ID follow-up

Database, World, Auth, Shared, Combat, Balance and Balance Service were rebuilt and rerun after
the deployment-owned App ID change. All seven passed. Together with the latest API run on the same
implementation, all eight Avalon test projects have **8056 passed, 2 existing skips, 0 failures**.
The skips remain the existing World and Shared cases. No runtime changes were needed for this gate.

### Application-bound Steam Playtest access

`storeAuthentication.steamPlaytest` defaults to disabled with AppID zero and no allowed worlds.
Deployments may prepare a disabled profile with a distinct positive uint32 AppID and distinct
positive uint16 `allowedWorldIds`; enabling it requires a nonempty restriction list. The chart
renders `SteamPlaytest__Enabled`, `SteamPlaytest__AppId` and indexed `SteamPlaytest__AllowedWorldIds__N`
under `Application__StoreAuthentication`, validating the same bounds as .NET startup.

The optional attempt `steamAppId` selects only a configured, enabled application. Omission means
the configured main app. A challenge binds this selection to proof, ownership evidence, linking,
receipt replay and renewal; clients cannot switch AppIDs within a context. License observations
are queried by the exact application as well as account/provider/product/environment. Apply migration
`20261004200634_ScopeSteamLicenseObservationsByApplication` during the coordinated Server rollout.

Verified Playtest ownership supplies the PTR permission only while evaluating a licensed Playtest
context's world eligibility. It applies to the configured allowed worlds and to both newly generated
and previously linked Avalon accounts, without changing stored account roles or the access level
sent to a world server. Main-app and Avalon-launcher contexts keep their existing account permissions;
the Playtest entitlement grants no staff or tournament permissions and cannot enter an unlisted world,
even for administrators or accounts owning both apps. Readiness, maintenance, protocol, account state,
character ownership and session checks still apply; only actual account privileges bypass maintenance.
Playtest world listing also requires current licensed authority, so pending or expired ownership cannot
expose PTR eligibility. Main-app and Avalon-launcher world listing retain their existing account-based behavior.

Allocation, ticket issuance/redemption and actual-world session fences recheck current configuration
and licensed context authority. Disabling Playtest, removing an allowed world or losing the verified
license refuses the corresponding context/admission/renewal. Legacy contexts with AppID
zero fail closed and require reauthentication. Homelab prepares Playtest 2514590 for PTR world 3,
disabled until compatible chart/releases and controlled Steam grants are ready. Old chart pins
ignore these values and must not be treated as deployment evidence.

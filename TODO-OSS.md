# OSS Readiness Checklist

Issues that must be addressed before making this repository public on GitHub.

---

## CRITICAL — Secrets & Credentials in Source

### OSS-001 — JWT Signing Key hardcoded in appsettings.json
**File:** `src/Server/Avalon.Api/appsettings.json:28`
```
"IssuerSigningKey": "GipaCjt1F8gPcBG5yLTkByVA2VEtjT5ZbY1sblRlFUaJUQA0vuXlPEMOCa7PvkgK"
```
This is a real-looking signing secret. Even if it is only used in development, publishing it trains bad habits
and could be reused in production.
**Fix:** Replace with a placeholder (`<CHANGE_ME_MIN_32_CHARS>`) and document that it must be set via environment
variable or secrets manager before running.

---

### OSS-002 — VAPID Public Key hardcoded in appsettings.json
**File:** `src/Server/Avalon.Api/appsettings.json:41`
```
"PublicKey": "BKNNdWAyeaORO8SVhNoMuHJhDHiyZ8BY37DiGTWpTA0gHFVqE1JfE7fiaP3_aMD_TE_Zu9q876dabCBM-VMNE5Q"
```
This is a real VAPID public key. The corresponding private key is blank here, but if the same key pair is used
in any deployment the private key should be rotated.
**Fix:** Replace with a placeholder and document how to generate a VAPID key pair.

---

### ~~OSS-003~~ — TLS certificate (PFX) committed to repository — **INTENTIONAL / RESOLVED**
The self-signed dev certificate has been consolidated to `certs/cert-tcp.pfx` (moved from root and per-project
duplicates removed). Committing it is **intentional** — it enables a zero-config contributor experience so
anyone can clone and run without generating certificates. This is documented in `CONTRIBUTING.md`.
The certificate is self-signed and dev-only; it must never be used in production.

---

### ~~OSS-004~~ — ~~Hardcoded private-network IP in docker-compose.yaml~~ — **DONE**
~~**File:** `src/Server/Avalon.Server.World/docker-compose.yaml`~~
Stale world-server compose file deleted entirely. Root `docker-compose.yml` consolidated to base infra only.

---

### ~~OSS-005~~ — ~~Registry URL leaks private container registry hostname~~ — **DONE**
~~**File:** `src/Server/Avalon.Server.World/docker-compose.yaml`~~
Resolved by deletion of the file above.

---

## HIGH — CI / Build Pipeline Issues

### OSS-006 — azure-pipelines.yml references a private Azure DevOps repository
**File:** `azure-pipelines.yml:36-39`
```yaml
repositories:
  - repository: BuildSteps
    type: git
    name: Avalon/BuildSteps
```
All pipeline templates (`dotnet/restore-solution.yml@BuildSteps`, etc.) are sourced from a private repo that
public contributors cannot access. The pipeline will fail for any fork.
**Fix:** Either inline the template steps, remove `azure-pipelines.yml` entirely (the GitHub Actions workflows
are sufficient for open-source CI), or document that this pipeline is for internal use only.

---

### ~~OSS-007~~ — `actions/checkout@v6` — **NOT AN ISSUE**
`v6` is a valid and current release (latest is `v6.0.2`, released January 2025). No action required.

---

### OSS-008 — CI pipeline grants `permissions: write-all` on the PR workflow
**File:** `.github/workflows/dotnet-pr.yml:10`
```yaml
permissions: write-all
```
Granting write-all permissions to a PR workflow is dangerous in a public repository: a malicious PR can use the
`GITHUB_TOKEN` to push to branches, modify releases, etc.
**Fix:** Scope permissions to the minimum required (typically `contents: read` for a build-only PR pipeline).

---

## MEDIUM — Configuration & Documentation

### ~~OSS-009~~ — Default credentials committed — **INTENTIONAL / RESOLVED**
Hardcoded local-dev credentials (`123` for Postgres/Redis, JWT signing key, cert password `avalon`) are
**intentional** to enable a zero-config contributor experience. This is documented in the "Zero-Config Dev
Environment" section of `CONTRIBUTING.md`.
The remaining open sub-item is to add env-specific appsettings patterns to `.gitignore` (see OSS-013)
to prevent accidental future leaks of production overrides.

---

### OSS-010 — No LICENSE file at repository root
`README.md:395` states "MIT (see repository root)" but no `LICENSE` file exists at the root (only
`vendor/DotRecast/LICENSE.txt` for the vendored dependency).
**Fix:** Add a `LICENSE` file (MIT) at the repository root.

---

### ~~OSS-011~~ — README references .NET 9 — **DONE**
Prerequisites updated to .NET 10 SDK; stray `net9` prose references in README also updated.

---

### ~~OSS-012~~ — ~~Stale MySQL docker-compose for world server~~ — **DONE**
File deleted as part of docker-compose consolidation.

---

## LOW — Hygiene

### ~~OSS-013~~ — `.gitignore` missing env-specific appsettings — **DONE**
Added `appsettings.Production.json`, `appsettings.Staging.json`, `appsettings.Local.json`, and
`appsettings.*.local.json` to `.gitignore`. `*.pfx` not excluded — dev cert in `certs/` is intentionally
committed (see OSS-003).

---

### OSS-014 — `azure-pipelines.yml` contains duplicate NuGet pack steps
`azure-pipelines.yml:65-77` packs `Avalon.Network.Packets` twice (identical block copy-pasted).
**Fix:** Remove the duplicate block (low urgency but confusing for contributors).

---

### OSS-015 — No `CONTRIBUTING.md` or GitHub issue/PR templates
README has a brief "Contributing" section but no dedicated `CONTRIBUTING.md` with issue/PR templates, coding
standards, or development environment setup guide for new contributors.
**Fix:** Add `CONTRIBUTING.md` and GitHub issue/PR templates under `.github/`.

- [x] `CONTRIBUTING.md` added at repo root
- [x] `.github/ISSUE_TEMPLATE/bug_report.md`
- [x] `.github/ISSUE_TEMPLATE/feature_request.md`
- [x] `.github/PULL_REQUEST_TEMPLATE.md`

---

## Summary

| ID | Severity | Category | One-liner |
|---|---|---|---|
| OSS-001 | CRITICAL | Secret | JWT signing key in appsettings.json |
| OSS-002 | CRITICAL | Secret | VAPID public key in appsettings.json |
| OSS-003 | ~~CRITICAL~~ | ~~Secret~~ | ~~PFX certificate committed to git~~ — **INTENTIONAL** (zero-config dev; moved to `certs/`, documented) |
| OSS-004 | ~~CRITICAL~~ | ~~Secret~~ | ~~Private LAN IP in docker-compose.yaml~~ — **DONE** |
| OSS-005 | ~~CRITICAL~~ | ~~Secret~~ | ~~Private registry domain in docker-compose.yaml~~ — **DONE** |
| OSS-006 | ~~HIGH~~ | ~~CI~~ | ~~azure-pipelines.yml depends on private `BuildSteps` repo~~ — **DONE** (file deleted) |
| OSS-007 | ~~HIGH~~ | ~~CI~~ | ~~`actions/checkout@v6` does not exist~~ — **NOT AN ISSUE** (v6.0.2 is current) |
| OSS-008 | ~~HIGH~~ | ~~CI~~ | ~~PR workflow has `permissions: write-all`~~ — **DONE** (scoped to `contents: read`) |
| OSS-009 | ~~MEDIUM~~ | ~~Config~~ | ~~Default password `123` everywhere~~ — **INTENTIONAL** (zero-config dev; documented in CONTRIBUTING.md) |
| OSS-010 | ~~MEDIUM~~ | ~~Legal~~ | ~~No LICENSE file at repo root~~ — **DONE** |
| OSS-011 | ~~MEDIUM~~ | ~~Docs~~ | ~~README says .NET 9, code targets .NET 10~~ — **DONE** |
| OSS-012 | ~~MEDIUM~~ | ~~Config~~ | ~~Stale MySQL docker-compose for world server~~ — **DONE** |
| OSS-013 | ~~LOW~~ | ~~Hygiene~~ | ~~`.gitignore` missing env-specific appsettings~~ — **DONE** |
| OSS-014 | ~~LOW~~ | ~~Hygiene~~ | ~~Duplicate NuGet pack step in azure-pipelines.yml~~ — **DONE** (file deleted) |
| OSS-015 | ~~LOW~~ | ~~Hygiene~~ | ~~No CONTRIBUTING.md or issue/PR templates~~ — **DONE** |

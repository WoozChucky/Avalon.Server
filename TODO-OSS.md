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

### OSS-003 — TLS certificate (PFX) committed to repository
**Files:**
- `cert-tcp.pfx` (repo root)
- `src/Server/Avalon.Server.Auth/cert-tcp.pfx`
- `src/Server/Avalon.Server.World/cert-tcp.pfx`

A private-key-bearing certificate is tracked in git. Even if it is self-signed/dev-only, the private key is
exposed to every future clone.
**Fix:**
1. Remove all `.pfx` files from git history (`git filter-repo` or BFG Repo Cleaner).
2. Add `*.pfx` and `*.p12` to `.gitignore`.
3. Document in README how to generate a self-signed dev certificate.
The certificate password (`"avalon"`) in `src/Server/Avalon.Server.Auth/appsettings.json:17` should also
become a placeholder.

---

### OSS-004 — Hardcoded private-network IP in docker-compose.yaml
**File:** `src/Server/Avalon.Server.World/docker-compose.yaml:18-33`
```
- "Database__Auth__Host=192.168.1.67"
```
This exposes your home/office LAN topology and is meaningless to external contributors.
**Fix:** Replace with `localhost` or a docker-network service name, or delete this file and replace it with a
template (`docker-compose.example.yaml`) that contributors fill in.

---

### OSS-005 — Registry URL leaks private container registry hostname
**File:** `src/Server/Avalon.Server.World/docker-compose.yaml:4`
```
image: registry.nunolevezinho.xyz/avalon/world-server:latest
```
This reveals your private registry domain.
**Fix:** Change to a generic placeholder (e.g., `<YOUR_REGISTRY>/avalon/world-server:latest`) or use the
GitHub Container Registry image name to match the CI pipeline.

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

### OSS-007 — GitHub Actions use `actions/checkout@v6` (does not exist)
**Files:** `.github/workflows/dotnet-ci.yml:20`, `.github/workflows/dotnet-pr.yml:15`
```yaml
uses: actions/checkout@v6
```
The latest stable release is `v4`. `v6` will fail at runtime.
**Fix:** Pin to `actions/checkout@v4`.

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

### OSS-009 — Default credentials documented/committed as `123` throughout
The password `123` appears in every appsettings file, docker-compose, and migration scripts as the default for
Postgres and Redis. While acceptable for local dev, it should be clearly labeled as a dev-only default and the
`.gitignore` should exclude environment-specific overrides.

**Affected files:**
- `src/Server/Avalon.Api/appsettings.json`
- `src/Server/Avalon.Server.Auth/appsettings.json`
- `src/Server/Avalon.Server.World/appsettings.json`
- `docker-compose.yml`
- `src/Server/Avalon.Server.World/docker-compose.yaml`
- `src/Server/Avalon.Database.Auth/add-migration.ps1` (and sibling scripts)

**Fix:** Add a comment (or README section) making explicit these are local-dev-only values. Also add
`appsettings.Production.json` and `appsettings.Staging.json` to `.gitignore` to prevent accidental future leaks.

---

### OSS-010 — No LICENSE file at repository root
`README.md:395` states "MIT (see repository root)" but no `LICENSE` file exists at the root (only
`vendor/DotRecast/LICENSE.txt` for the vendored dependency).
**Fix:** Add a `LICENSE` file (MIT) at the repository root.

---

### OSS-011 — README references .NET 9 SDK in prerequisites but codebase targets .NET 10
`README.md:305`: "Prerequisites: .NET 9 SDK"
`CLAUDE.md`: "Target framework: .NET 10"
**Fix:** Update README prerequisites to .NET 10.

---

### OSS-012 — `src/Server/Avalon.Server.World/docker-compose.yaml` references old MySQL config
Port `3306` and `Username=root` suggest a MySQL-era config; the rest of the codebase uses Postgres on `5432`.
This file appears to be stale / never updated after the MySQL → Postgres migration.
**Fix:** Either update to match current Postgres config or delete the file entirely.

---

## LOW — Hygiene

### OSS-013 — `.gitignore` does not exclude `*.pfx` / `*.p12` certificate files
After removing the committed certificates (OSS-003), prevent future accidents.
**Fix:** Add to `.gitignore`:
```
*.pfx
*.p12
*.key
appsettings.Production.json
appsettings.Staging.json
appsettings.Local.json
```

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
| OSS-003 | CRITICAL | Secret | PFX certificate (with private key) committed to git |
| OSS-004 | CRITICAL | Secret | Private LAN IP in docker-compose.yaml |
| OSS-005 | CRITICAL | Secret | Private registry domain in docker-compose.yaml |
| OSS-006 | ~~HIGH~~ | ~~CI~~ | ~~azure-pipelines.yml depends on private `BuildSteps` repo~~ — **DONE** (file deleted) |
| OSS-007 | HIGH | CI | `actions/checkout@v6` does not exist |
| OSS-008 | ~~HIGH~~ | ~~CI~~ | ~~PR workflow has `permissions: write-all`~~ — **DONE** (scoped to `contents: read`) |
| OSS-009 | MEDIUM | Config | Default password `123` everywhere — needs clear labeling |
| OSS-010 | ~~MEDIUM~~ | ~~Legal~~ | ~~No LICENSE file at repo root~~ — **DONE** |
| OSS-011 | MEDIUM | Docs | README says .NET 9, code targets .NET 10 |
| OSS-012 | MEDIUM | Config | Stale MySQL docker-compose for world server |
| OSS-013 | LOW | Hygiene | `.gitignore` missing `*.pfx`, `*.p12`, env-specific appsettings |
| OSS-014 | LOW | Hygiene | Duplicate NuGet pack step in azure-pipelines.yml |
| OSS-015 | ~~LOW~~ | ~~Hygiene~~ | ~~No CONTRIBUTING.md or issue/PR templates~~ — **DONE** |

# README Refactor — Design Spec

**Date:** 2026-04-08  
**Status:** Completed  
**Goal:** Make README digestible for OSS contributors; move deep-dive content to focused docs.

---

## Problem

The README is 424 lines and mixes "front door" content with deep technical reference material. Sections like the full packet protocol lifecycle, server bootstrap steps, and OpenAPI transformer internals belong in the docs folder — not in the first file a contributor reads.

## Scope

**Option B (Medium):** Keep high-level architecture and solution structure. Move deep dives. README targets ~160 lines.

---

## Changes

### README (refactored)

Sections to **remove** (content moves to dedicated docs):
- Custom Packet Protocol (~90 lines + Mermaid diagram) → `docs/networking-packet-protocol.md`
- Project Startup Flow → `docs/architecture-startup-flow.md`
- Handling ValueObject\<T\> in OpenAPI → `docs/valueobject-openapi.md`
- Extending the Domain → `CONTRIBUTING.md`
- Quick FAQ → **dropped entirely**
- Contributing bullet list → replaced with one-line pointer to `CONTRIBUTING.md`

Sections to **keep and condense**:
- Title + tagline — unchanged
- High-Level Overview — trim slightly, keep communication paths
- Solution Structure — keep project table; one-line descriptions
- Core Cross-Cutting Concepts — all subsections stay, each reduced to 2–3 sentences; add `See →` links to deep-dive docs
- Running Locally — unchanged
- Migrations Workflow — keep key command + design-time note; drop PowerShell script mention
- Testing — one line
- Benchmarking — one line
- Feature Documentation table — expand with 3 new rows for new docs
- Roadmap — unchanged
- License — unchanged

---

### New Docs

#### `docs/networking-packet-protocol.md`
Content: the full "Custom Packet Protocol" section from README verbatim.  
Includes: header field definitions, 15-step auth-phase lifecycle, world handoff steps, Redis key patterns used in flow, failure modes, security considerations, extensibility points, Mermaid sequence diagram, textual summary.

#### `docs/architecture-startup-flow.md`
Content: the "Project Startup Flow" section from README verbatim.  
Includes: numbered bootstrap steps for API, Auth Server, and World Server.

#### `docs/valueobject-openapi.md`
Content: the "Handling ValueObject\<T\> in OpenAPI" section from README verbatim.  
Includes: transformer pattern, registration via `AddSchemaTransformer`, schema shape transformation logic.

#### `CONTRIBUTING.md` (new, repo root)
Content:
- Contributing guidelines (fork & branch, run tests & benchmarks pre-PR, keep abstractions in Public projects, update docs on structural changes)
- "Extending the Domain" section (add ValueObject, add packet handler, add world script)
- No FAQ

---

## Feature Documentation Table (updated)

The README's existing table gains three new rows:

| Document | Description |
|---|---|
| Networking — Packet Protocol | Header fields, auth lifecycle, world handoff, Redis patterns, failure modes |
| Architecture — Startup Flow | Bootstrap sequence for API, Auth Server, and World Server |
| ValueObject — OpenAPI Integration | Schema transformer pattern, registration, and shape transformation |

---

## Out of Scope

- Changes to existing docs content
- Any code changes
- Restructuring the docs folder beyond adding new files

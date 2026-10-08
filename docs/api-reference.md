---
title: API Reference
template: api-reference.html
search:
  # The rendered page is Scalar's, not this body, so indexing this text would put
  # notes-to-maintainers in the site search.
  exclude: true
---

The page itself is rendered by `overrides/api-reference.html`, which replaces
Material's chrome with Scalar's so the reference gets the full window. This body
is not displayed; the notes below are for whoever edits the page next.

The document it reads, `api/openapi.json`, is emitted by `Avalon.Api`'s own build
(`Microsoft.Extensions.ApiDescription.Server`) and copied into the site by
`.github/workflows/docs.yml`. No server, database or cache is involved
(`AVALON_OPENAPI_GENERATION_ONLY=true`). The build sets no `Application:Services`, so
the host runs all four API services (identity, worlds, commerce and distribution, see
`api-services.md`) and the document is the whole contract. A running process in
Development (or with `Application:ApiDocs:Enabled`; production serves none, #803) serves at
`/openapi/v1.json` only the routes of the services it runs; the one that runs all four
serves this document, which differs only in having no `servers` block, because at build
time there is no address to report.

That URL is stable, and it is what the `Avalon.Dashboard` repository's client
generator checks itself against. A push to `main` that touches the API host or any of
its libraries (`src/Server/Avalon.Api*/**`) republishes it, as one to the docs does.

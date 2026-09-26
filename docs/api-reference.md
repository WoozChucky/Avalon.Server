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
`.github/workflows/docs.yml`. No server, database or cache is involved. It differs
from the document a running instance serves at `/openapi/v1.json` only in having no
`servers` block, because at build time there is no address to report.

That URL is stable, and it is what the `Avalon.Dashboard` repository's client
generator checks itself against.

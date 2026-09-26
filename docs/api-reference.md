# REST API reference

The reference below is generated from `Avalon.Api`'s own OpenAPI document and
rebuilt whenever the API's source changes, so it describes the contract this
branch actually serves rather than a hand-maintained copy of it.

The raw document is published alongside this page at
[`api/openapi.json`](api/openapi.json). That URL is stable, and it is what the
`Avalon.Dashboard` repository's client generator checks itself against — if the
two disagree, the dashboard's drift job opens a pull request there with the
refreshed contract.

!!! note "How this is generated"

    `Microsoft.Extensions.ApiDescription.Server` writes the document during
    `dotnet build`, without a running server, a database or Redis. The generator
    does boot the host to read it, so the docs workflow supplies a throwaway
    `Application__Authentication__IssuerSigningKey` to satisfy the startup guard
    described in [Configuration](configuration-reference.md). That key signs
    nothing.

    One difference from the document served at `/openapi/v1.json` by a running
    instance: this one carries no `servers` block, because at build time there is
    no address to report.

<div id="api-reference"></div>

<script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.72.1"></script>
<script>
  // MkDocs rewrites relative links in Markdown but not inside a script block, and
  // this page is served from /api-reference/, so the path has to step back out.
  // Keeping it relative also survives the project being served from a subpath.
  Scalar.createApiReference('#api-reference', {
    url: '../api/openapi.json',
    theme: 'purple',
  })
</script>

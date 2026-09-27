## What

<!-- A concise description of the change. -->

## Why

<!-- The motivation: bug fix, feature, refactor, etc. Link to the related issue if one exists (e.g. Closes #123). -->

## How

<!-- Brief explanation of the approach. Call out non-obvious decisions or trade-offs. -->

## Player note

Player note: <!-- Required. Written like a patch note: third person, starting with what changed. E.g. "Fixed an issue where heals could raise health above the maximum." / "Added browser sign-in to the launcher." / "Increased the world server's connection limit." For internal work: "No gameplay changes: faster builds." Shown on the public changelog. -->

## Checklist

- [ ] Tests pass (`dotnet test --no-build`)
- [ ] Solution builds in Release (`dotnet build -c Release`)
- [ ] No credentials, keys, or connection strings added to source
- [ ] New public types/interfaces placed in the appropriate `*.Public` or `*.Abstractions` project
- [ ] Related issue linked, and any follow-up worth tracking filed as an issue

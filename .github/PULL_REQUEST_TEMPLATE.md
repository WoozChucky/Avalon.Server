## What

<!-- A concise description of the change. -->

## Why

<!-- The motivation: bug fix, feature, refactor, etc. Link to the related issue if one exists (e.g. Closes #123). -->

## How

<!-- Brief explanation of the approach. Call out non-obvious decisions or trade-offs. -->

## Player note

Player note: <!-- Required: one plain sentence a player would understand. What will they notice? For internal work, say what it means, e.g. "Faster builds; nothing changes in the game." It is shown on the public changelog. -->

## Checklist

- [ ] Tests pass (`dotnet test --no-build`)
- [ ] Solution builds in Release (`dotnet build -c Release`)
- [ ] No credentials, keys, or connection strings added to source
- [ ] New public types/interfaces placed in the appropriate `*.Public` or `*.Abstractions` project
- [ ] Related issue linked, and any follow-up worth tracking filed as an issue

# Tooling

`tools/` holds the offline utilities. They are not part of the server's runtime and nothing in
`src/Server` depends on them.

| project | what it does |
| --- | --- |
| `Avalon.Benchmarking` | BenchmarkDotNet harnesses |
| `Avalon.ChunkImporter` | imports chunk templates into the database |
| `Avalon.SchemaGen` | emits the wire schema the client vendors |
| `Avalon.ChunkRotationVectors` | emits `ChunkRotation.LocalToWorld` known-answer vectors for the client's mirror |

## Wanted: one prototypes exporter, not one project per artifact

Three of the four above exist because a client needed a specific artifact exported from the server's
own types, and each arrived as its own `csproj`. That does not scale: the next vendored artifact —
ability rows, item prototypes, spell data, loot tables — would add a fifth and a sixth by the same
reasoning, each with its own duplicated database/config bootstrap and its own output convention.

What is wanted instead is **one exporter with a subcommand per artifact**, sharing the host
builder, the connection setup and the output format, so that adding an export is a command rather
than a project. `SchemaGen` and `ChunkRotationVectors` are the two clearest candidates to fold in
first — both read server types and write a file the client vendors and hashes.

Not done here deliberately: this PR adds the vectors the client's rotation mirror is already held
to, and merging the tools is a refactor that should not ride along with the thing that motivated it.

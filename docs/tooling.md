# Tooling

`tools/` holds the offline utilities. They are not part of the server's runtime and nothing in
`src/Server` depends on them.

| project | what it does |
| --- | --- |
| `Avalon.Benchmarking` | BenchmarkDotNet harnesses |
| `Avalon.Exporter` | exports every artifact the client vendors, one subcommand per artifact |

## The exporter

Three projects used to sit here because a client needed a specific artifact exported from the
server's own types, and each arrived as its own `csproj` with its own bootstrap and its own output
convention. That did not scale: the next vendored artifact would have added a fourth by the same
reasoning. `Avalon.SchemaGen` and `Avalon.ChunkRotationVectors` are now one project, and adding an
export is an entry in `Exports.All` rather than a new project.

```bash
dotnet run --project tools/Avalon.Exporter                    # lists the artifacts, writes nothing
dotnet run --project tools/Avalon.Exporter -- all             # writes all nine
dotnet run --project tools/Avalon.Exporter -- proto corpus    # writes just those
dotnet run --project tools/Avalon.Exporter -- all --out /tmp  # somewhere other than schema/
```

| name | writes | from |
| --- | --- | --- |
| `proto` | `schema/avalon.proto` | the `[ProtoContract]` packet types |
| `opcodes` | `schema/opcodes.json` | the opcode and encryption-flag reflection |
| `corpus` | `schema/corpus/*.txt` | the server's serializer, one file per message |
| `crypto` | `schema/crypto/session-v1.txt` | the production `AvalonCryptoSession` and `SessionKeys` |
| `rotation` | `schema/vectors/rotation-v1.txt` | `ChunkRotation.LocalToWorld` via the real layout generator |
| `object-guid` | `schema/vectors/object-guid-v1.txt` | `ObjectGuid`'s own shifts and masks |
| `navmesh` | `schema/vectors/navmesh-v1.txt` | the DotRecast bake and movement queries from the real generator |
| `item-schema` | `schema/items/item-schema-v1.json` | `ItemTemplate` and its eight enumerations |
| `item-catalog` | `schema/items/item-catalog-v1.json` | the item template rows — **needs a World database** |
| `ability-catalog` | `schema/abilities/ability-catalog-v1.json` | every ability row a client names and draws, creatures' included (#163) — **needs a World database** |
| `quest-catalog` | `schema/quests/quest-catalog-v1.json` | every quest's enUS title, stages, objectives and rewards, for tooling (#714) — **needs a World database** |

`item-catalog`, `ability-catalog` and `quest-catalog` read their World connection string only from the environment
(`Database__World__ConnectionString`) or from user-secrets for `src/Server/Avalon.Database.World`,
the sources the `dotnet ef` design-time factories use (#557). None reads an `appsettings` file,
so running one from a folder that holds one cannot point it at that file's database. Without a
string, a call that names any of them (`all` included) stops before anything is written and says a World
database connection is needed.

Two rules the tool keeps, because both failures are silent ones:

- **Every name is resolved before anything is written.** A typo cannot export nine of ten artifacts
  and report the failure afterwards, leaving a tree nobody asked for.
- **Everything is written with explicit LF.** These files are hashed as bytes at the other end, so
  a CRLF is not a formatting nit — it is a hash the client cannot reproduce. `.gitattributes` holds
  the same line from the other side.

The exporter is two projects: `Avalon.Exporter.Emitters` holds everything derived from the code
alone and is what the drift tests reference, and `Avalon.Exporter` adds the CLI and the two exports
that read a database. The split keeps EF Core and Npgsql out of the shared unit tests, which
reference the emitters only.

Nothing is exported from a transcription. Each artifact runs the server's own type, because a
re-typed constant agrees with whatever it was typed from — which is the failure these files exist
to catch.

## What is not in it

`Avalon.Benchmarking` is a harness that owns its own `Main`; it does not belong behind an export
subcommand. Chunk data is not exported either: `tools/Avalon.ChunkGen` generates the forest pieces and
the town's squares straight into `src/Server/Avalon.Server.World/Maps/`, beside the chunk files and the town
layout kept there as they are, and the World server seeds the database from there on
start (see [map-generation.md](map-generation.md)). `Avalon.ChunkGen` stays a tool of its own because
it writes the server's source data, the chunk catalog the World server reads, not an artifact derived
for clients; how to run it is under "Generated forest chunks" in map-generation.md.

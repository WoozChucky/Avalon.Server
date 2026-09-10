# Wire schema

The language-neutral definition of the Avalon TCP protocol, exported from the C# packet
contracts so a non-.NET client can be built against it.

| File | What it is |
|---|---|
| `avalon.proto` | Generated. Every `[ProtoContract]` type in `Avalon.Network.Packets` and `Avalon.Network.Packets.Abstractions`, as proto3. |
| `opcodes.json` | Generated. The opcode-to-message mapping, the per-packet encryption flags, and the entity-field bitmask — none of which a `.proto` can express. |
| `protobuf-net/bcl.proto` | Vendored, not generated. See below. |

Regenerate both after any change to a packet contract:

```bash
dotnet run --project tools/Avalon.SchemaGen
```

`WireSchemaShould` in `tests/Avalon.Shared.UnitTests` regenerates and compares, so forgetting
to is a failing test rather than a client that decodes into the wrong field.

## Why this lives at the repository root

It is consumed from outside this repository — the C++ client compiles it with `protoc` — so
it is not filed under `src/`, which is C# layout. `protoc -I schema schema/avalon.proto` is
the whole invocation; the include root is this directory, which is why `bcl.proto` sits under
a `protobuf-net/` subdirectory matching the import path the generator emits.

Verified with `protoc 32.0`: `--cpp_out` compiles with no errors and nothing on stderr.

## What the schema cannot carry

**Opcodes.** Packet identity is not data anywhere in the server. It is a `public static
NetworkPacketType` field on each packet class, found by reflection at startup, and a C++
client cannot reflect. `opcodes.json` performs that reflection once, at export time.

**Encryption flags.** Likewise a static field per class, and a client must know per opcode
whether to encrypt. Seven packet classes declare no flags field; those entries carry `null`
rather than a guess.

Two header fields are deliberately absent from `opcodes.json`, because building on them would
propagate a fiction: `NetworkProtocol` is read by nothing, and `Version` is hardcoded to zero
at every construction site.

**The entity-field bitmask.** `GameEntityFields` selects which fields an entity-state packet
carries, but it travels inside an opaque `bytes` member, so the schema describes neither the
blob nor the mask that heads it. It could not be an enum in the `.proto` either: proto3 requires
the first member to be zero, and this one starts at `None = 1 << 0`. The `entityFields` section
exports it instead, split into the single-bit members (`bits`, each with its bit index) and the
combinations the server names (`masks`, each listing the bits it sets).

Two things a reader must not assume. `hasZeroValue` is `false` — no member is zero, so a client
that writes a zero-based flags enum is wrong about every bit, and the numbers have to be copied
rather than regenerated. And two of the bits are decorative: `CreatureMetadataId` and `Name` are
declared and set in `All`, but the writer emits both values unconditionally and tests neither.

## The vendored `bcl.proto`

Five fields — two chat timestamps and three map-instance ids — serialize through
protobuf-net's own representations rather than the `google.protobuf` well-known types, so the
schema imports `protobuf-net/bcl.proto`. That file ships in neither the NuGet package nor the
protobuf distribution, so a copy is kept here, taken verbatim (line endings normalized to LF)
from `src/Tools/bcl.proto` in the protobuf-net repository, which is Apache-2.0 licensed.

`bcl.DateTime` is a scaled offset from the Unix epoch and `bcl.Guid` is two `fixed64`s in
.NET's byte order rather than sixteen bytes in RFC order, so a consumer must convert. One that
assumes the well-known types reads plausible wrong values and reports no error.

Whether those five fields could instead become `int64` unix-millis and `bytes`, deleting the
dependency at the cost of a wire break, is open. Nothing has established whether anything
depends on the `Guid` form specifically — a database column, for instance.

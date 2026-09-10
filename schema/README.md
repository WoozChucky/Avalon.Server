# Wire schema

The language-neutral definition of the Avalon TCP protocol, exported from the C# packet
contracts so a non-.NET client can be built against it.

| File | What it is |
|---|---|
| `avalon.proto` | Generated. Every `[ProtoContract]` type in `Avalon.Network.Packets` and `Avalon.Network.Packets.Abstractions`, as proto3. |
| `opcodes.json` | Generated. The opcode-to-message mapping, the per-packet encryption flags, and the entity-field bitmask — none of which a `.proto` can express. |
| `corpus/*.txt` | Generated. One file per message, holding the bytes the server writes for a set of deliberately awkward values. |
| `protobuf-net/bcl.proto` | Vendored, not generated. See below. |
| `protobuf-net/NOTICE` | Where that copy came from, under what license, and which library version it matches. |

Regenerate all of them after any change to a packet contract:

```bash
dotnet run --project tools/Avalon.SchemaGen
```

`WireSchemaShould` and `WireCorpusShould` in `tests/Avalon.Shared.UnitTests` regenerate and
compare, so forgetting to is a failing test rather than a client that decodes into the wrong
field.

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

## The golden corpus

The schema being generated from the contracts and guarded against drifting from them means it
*describes* them. It does not mean the bytes agree — a reader generated from the schema can
parse what the server writes, re-encode it, and produce something different, with nothing on
either side raising anything. `corpus/` closes that gap: for every message, the bytes the
server's serializer actually produces for values chosen to make an encoding decision
observable.

`tests/Avalon.Wire.Reference` compiles this schema with `protoc` at build time, and
`WireRoundTripShould` holds every vector in the corpus to four things: a reader generated from
the schema can parse it, re-encoding produces the same bytes, the server reads back what that
reader writes, and every field arrives in the member it left. Comparison is on bytes rather
than through protobuf's text format, so NaN, the infinities, denormals and negative zero are
compared as the bit patterns they are rather than as decimal renderings of them.

A client can use the corpus without a .NET runtime. Each file is line-oriented:

```
message CAuthPacket           the message these vectors belong to

variant empty                 one shape of it; the name is a label, not a wire concept
  field 1 Username : string = ""  (0 chars, 0 bytes utf-8)
  field 2 Password : string = ""  (0 chars, 0 bytes utf-8)
  bytes 4                     how many bytes the encoding is
    0a 00 12 00               the bytes, lowercase hex, sixteen per line
```

Lines beginning `#` are commentary, `field` lines are the values the bytes encode (indented
further for a submessage or a repeated element), and the lines indented under `bytes` are the
vector. The variants are `absent` (nothing set), `empty` (everything present and carrying
nothing), `populated`, `maxima`, `minima`, `varint-edges` (values and lengths astride a varint
width boundary) and, for the messages that carry a float somewhere, `float-specials`.

The fixtures are generated by reflection over the contracts rather than written by hand, so a
member added to a packet is covered without anyone remembering to cover it. Their values are
picked from a ladder per type and indexed by a hash of the member's name, which makes them
varied between members and identical between runs.

## Where the bytes and the schema part company

Two differences are real, neither corrupts a value, and neither raises anything. They are
pinned down with their bytes in `WireLimitsShould`, alongside the case that used to be a third
and no longer is.

**An empty string or byte array used to be indistinguishable from a missing one, and is not
any more.** protobuf-net writes a two-byte field for `""` and for `Array.Empty<byte>()`,
because in C# those are not null, and plain proto3 gives a singular `string` or `bytes` field
no way to say "present and empty" — a reader generated from such a schema decoded an empty one
and an absent one alike and wrote back neither. This was not hypothetical:
`SWorldSelectPacket.CreateError` sends `WorldKey = Array.Empty<byte>()` on every
duplicate-session rejection, eleven string members initialize to `string.Empty`, and
`ObjectAdd.Fields` and `ObjectUpdate.Fields` are `ReadOnlyMemory<byte>` — a value type that
cannot be null, so the server writes an empty field for it even where nothing was assigned, on
the entity-replication path.

Every singular `string` and `bytes` field therefore carries `optional`, which gives proto3
explicit presence and does not change how a present value encodes, so it costs no bytes. Note
what the criterion is not: C# nullability does not predict any of this, since a
`ReadOnlyMemory<byte>` is never null and is written every time. It is what protobuf-net puts on
the wire that decides, not how the member is declared.

**A `DateTime` does not carry its kind.** protobuf-net writes `bcl.DateTime`'s value and scale
and never its `kind` field, so a UTC timestamp is indistinguishable on the wire from an
unspecified one, and the server's own deserializer reads it back as `Unspecified`. Both chat
timestamps are affected. A client has to be told out of band which zone they are in.

**Negative zero cannot be sent.** IEEE says it equals zero and proto3 omits a float field that
equals its default, so both encoders drop it and both read back positive zero. Every other
float value — including NaN with a payload, both infinities and denormals — survives bit for
bit in both directions.

One further asymmetry belongs to the server alone and involves no schema. A submessage member
that the contract initializes cannot stay absent through a round trip: deserializing bytes that
never carried it still leaves the member set, and serializing again writes an empty submessage.
`Serialize(Deserialize(x))` is therefore not always `x`, for protobuf-net on its own.

## The vendored `bcl.proto`

Five fields — two chat timestamps and three map-instance ids — serialize through
protobuf-net's own representations rather than the `google.protobuf` well-known types, so the
schema imports `protobuf-net/bcl.proto`. That file ships in neither the NuGet package nor the
protobuf distribution, so a copy is kept here, taken verbatim (line endings normalized to LF)
from `src/Tools/bcl.proto` in the protobuf-net repository. It is Apache-2.0 while Avalon is MIT,
so it carries its own `NOTICE` recording the source, the author, the license, and the library
version and upstream commit the copy matches.

Nothing can compare that copy to its original: there is no original to fetch, which is why it
is vendored in the first place. So `VendoredBclSchemaShould` holds the version recorded in the
`NOTICE` to the one `src/Directory.Packages.props` references, and a protobuf-net upgrade fails
until someone has re-read the two files against each other. That is a prompt, not a comparison —
it cannot tell you the file changed, only that it might have.

`bcl.DateTime` is a scaled offset from the Unix epoch and `bcl.Guid` is two `fixed64`s in
.NET's byte order rather than sixteen bytes in RFC order, so a consumer must convert. One that
assumes the well-known types reads plausible wrong values and reports no error.

Whether those five fields could instead become `int64` unix-millis and `bytes`, deleting the
dependency at the cost of a wire break, is open. Nothing has established whether anything
depends on the `Guid` form specifically — a database column, for instance.

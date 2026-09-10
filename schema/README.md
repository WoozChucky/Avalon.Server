# Wire schema

The language-neutral definition of the Avalon TCP protocol, exported from the C# packet
contracts so a non-.NET client can be built against it.

| File | What it is |
|---|---|
| `avalon.proto` | Generated. Every `[ProtoContract]` type in `Avalon.Network.Packets` and `Avalon.Network.Packets.Abstractions`, as proto3. |
| `opcodes.json` | Generated. The opcode-to-message mapping and the per-packet encryption flags — neither of which a `.proto` can express. |
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

**Entity kinds.** Which kind of entity an `ObjectState` describes is the top byte of its
identifier rather than a member of any message, so no `.proto` can declare the values. They are
written out in the next section, together with the rest of what reading entity state takes.

Nothing else is missing. Entity state itself used to be a fourth entry here, because it
travelled as an opaque `bytes` member headed by a bitmask the schema could not describe and a
client had to copy by hand. It is `ObjectState` in `avalon.proto` now, every member carrying
its own presence, so a client reads which members arrived instead of decoding a mask to find
out. The bitmask still exists on the server as its record of what has changed, and is no longer
exported: a client that implemented it would be implementing something it must not use.

## Entity state

Three packets replicate the world. `SMSG_WORLD_STATE_ADD` carries entities the client has not
seen before, `SMSG_WORLD_STATE_UPDATE` carries changes to entities it already has, and both are
a list of `ObjectState`. `SMSG_WORLD_STATE_REMOVE` carries bare identifiers. One message serves
every kind of entity, so a reader parses the same message either way and then asks which
members arrived.

Every member but `Guid` is optional, and **absent is not zero**. A member is present when the
server has something to say about it. `CurrentHealth = 0` is a unit at zero health; no
`CurrentHealth` member means nothing was said about it and the client keeps the value it
already holds. **Presence does not mean the value changed**, either — the server sends a fixed
selection per kind rather than a per-field delta, so a member arrives on every update whether
or not it moved.

### Which members each kind sets

| Member | Character | Creature | Projectile | Portal |
|---|:-:|:-:|:-:|:-:|
| `Guid` | ✓ | ✓ | ✓ | ✓ |
| `Position` | ✓ ¹ | ✓ | ✓ | ✓ |
| `Velocity` `Orientation` | ✓ ¹ | ✓ | ✓ | |
| `MoveState` `Health` `CurrentHealth` `Level` | ✓ | ✓ ² | | |
| `IsDead` | ✓ | ✓ ² ³ | | |
| `PowerType` | ✓ | ✓ | | |
| `Power` `CurrentPower` | ✓ ⁴ | ✓ ⁴ | | |
| `Experience` `RequiredExperience` | ✓ | | | |
| `CreatureMetadataId` | | ✓ | | |
| `Name` | ✓ | ✓ | | |
| `PortalRadius` `PortalTargetMapId` `PortalRole` | | | | ✓ |

¹ **A character's own client does not receive its placement.** `Position`, `Velocity` and
`Orientation` are withheld from the one client that owns the character, because they reach it
on a different packet. Every other client receives them. Absent here means *not for you*, not
*at the origin and stationary* — a client that read it the second way would teleport the player
to the origin ten times a second.

² On an add only. A creature update carries neither `Health` nor `Level` nor `IsDead`, though a
creature add carries all three. Which members a selection contains is a server decision that
can narrow without the schema changing, so treat every optional member as possibly absent in
any message rather than deriving a kind's shape from one capture.

³ A creature is always alive: it sends `IsDead = false` rather than nothing, because the member
is filled from a selection shared with characters. Read it as *no information*, not as a
creature that cannot die.

⁴ Withheld together when `PowerType` is `PowerType_None`. A unit that spends nothing has no
pool to report, so a client that reads `PowerType_None` should expect neither amount, whatever
it received before.

A portal never appears in an update: portals do not change while they exist. `PortalRadius` is
how close a character must be for the portal to take them, and `PortalRole` is `0` back or `1`
forward. `CreatureMetadataId` is the template a creature was spawned from. `Orientation` is a
single angle, the yaw — nothing in this world leans, so pitch and roll are not replicated.

### Reading the identifier

`Guid` is not an opaque number. The kind of entity is packed into its top byte, and the kind
decides which of the members above can arrive, so a client reads the kind before it reads the
message:

```
kind = (guid >> 56) & 0xFF
id   = guid & 0xFF_FFFF_FFFF     unique within a kind, not across kinds
```

The forty low bits hold the id, of which the server fills thirty-two today, and the sixteen
bits between the id and the kind are unused and always zero. The kinds are `1` character, `2`
creature, `4` projectile and `5` portal; `0` is the empty identifier and `3` is a spell, which
is never replicated as an entity. **Neither the layout nor
those numbers appear anywhere a non-.NET reader can find them** — not in `avalon.proto`, not in
`opcodes.json` — so a client hardcodes them and has no way to notice a renumber. `MoveState`
and `PowerType` are exported because they are members of a message; entity kinds are not,
because they are not members of anything.

The identifiers in `SMSG_WORLD_STATE_REMOVE` are the same values and decompose the same way.

### A position is present or absent as a whole

`Position` and `Velocity` are `Vec3` submessages rather than three floats on `ObjectState`, and
that is deliberate: three members would have made *has not moved* and *is at the origin* the
same bytes. The triple arrives together or not at all; there is no message carrying an X and a
Y but no Z.

Inside a `Vec3` the components are plain proto3 floats with no presence of their own, and
proto3 omits a field that equals its default. `(0, 3.5, 0)` therefore encodes `Y` alone, and a
position at the origin encodes to a present submessage of **zero bytes** — see the `absent` and
`empty` variants in `corpus/Vec3.txt`, which are both empty. **A missing component means zero,
never unknown.** A reader that treats a missing `X` the way it treats a missing `Position`
rejects legitimate coordinates, and one coordinate at zero is ordinary rather than rare.

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

## What the corpus does not cover

Every gap below is known and none of them is accidental. They are written down so that extending
the corpus starts from them instead of rediscovering them one at a time.

**No vector mixes member states.** A variant sets every member of a message to the same state —
all absent, or all empty, or all at their maximum. Nothing in the corpus is a message with some
members present and others missing, which is what the whole family of reader-robustness cases
needs: a present tag with earlier tags absent, tags out of order, a repeated tag, a field number
the schema does not declare, a wire type that disagrees with the declared one, and input that
stops mid-field. Every vector is well-formed output from one encoder, written in ascending tag
order, so a reader that handles nothing else passes all of them.

**A present submessage holding a present-but-empty string is never produced by a parent.** The
`empty` variant fills a singular submessage member by building the submessage as `absent`, so the
parent's vectors show that member present and carrying nothing at all. The shape explicit
presence exists to protect — an empty-but-present string inside a present parent — appears only
in the child message's own file, never nested inside anything.

**The only three-element vectors hold three identical elements.** A repeated member gets three
elements under `maxima` and one under `minima`, and those two variants are a single value per
type that ignores an element's ordinal. Element ordering therefore cannot be tested where the
count is highest. The two-element `populated` and `varint-edges` vectors do fold the ordinal in,
and are where a reader that returns elements in the wrong order can show itself.

**An undeclared enum value is always a positive one.** The value chosen for `varint-edges` is
found by searching upward from the largest declared member and only turns downward when nothing
above it fits, which has not happened for any enum in the protocol. So the ten-byte varint a
negative enum value encodes to is in no vector.

**Adding one float member rewrites float-specials vectors across the whole corpus, and that is
the mechanism working.** Each float member is handed a special by its position in one globally
sorted list of every float member in the protocol, which is what guarantees that all eight
specials appear somewhere rather than probably appearing. A new float member shifts the position
of every member sorting after it, and their specials rotate with it. Expect a large diff and read
it as churn, not as drift.

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
duplicate-session rejection, and eleven string members initialize to `string.Empty`.

Every singular `string` and `bytes` field therefore carries `optional`, which gives proto3
explicit presence and does not change how a present value encodes, so it costs no bytes. Note
what the criterion is not: C# nullability does not predict any of this. The sharpest case used
to be on the entity-replication path, where two members were `ReadOnlyMemory<byte>` — a value
type, never null, so the server wrote an empty field for one even where nothing had been
assigned. Those two members are gone with the payload they carried, and no contract declares
that type today, but the rule still recognises it, because it is what protobuf-net puts on the
wire that decides and not how the member is declared.

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

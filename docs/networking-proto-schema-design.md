# Wire Schema as `.proto` — Design Spec

**Date:** 2026-09-10
**Status:** Approved (awaiting plan). Branch target: `feat/proto-wire-schema`.
**Scope:** making a generated `.proto` the shared definition of the wire protocol, and replacing the entity-field blob that a schema cannot describe.
**Repos read:** `Avalon.Server` at `1c6db2d8`, the Unity client at `C:\dev\3D`, and the C++ engine at `C:\dev\vulkan-multithreaded` (`bf1002c`). Every figure below was measured on 2026-09-10 against those revisions.
**Clients:** the Unity client is being **retired**; the C++/Vulkan engine replaces it and has **no netcode of any kind** today.

---

## 1. Goal & Motivation

Make a language-neutral `.proto` schema the shared, machine-checked definition of the wire protocol, so that the C# server and a future C++ client both derive their packet definitions from one artifact instead of two people keeping two copies aligned by review.

### 1.1 There is no schema artifact today, and the copy has already drifted

The protocol is defined by attributes on C# classes — `[ProtoContract]` / `[ProtoMember(N)]` under `src/Shared/Avalon.Network.Packets/` — serialized by **protobuf-net 3.4.21** (`src/Directory.Packages.props:55`). That is Marc Gravell's library, not `Google.Protobuf`: there is no `protoc`, no `Grpc.Tools`, and **no `.proto` file anywhere in either repository**. The bytes are standard protobuf; what is missing is anything to compile against.

Measured by reflection over the built `netstandard2.1` assemblies:

| Metric | Count |
|---|---|
| `[ProtoContract]` types | **93** (81 wire packet classes, 10 nested DTOs, 2 envelope) |
| `[ProtoMember]` fields | **231** (matches 231 source lines exactly) |
| Enums reachable from contracts | **13** |
| `NetworkPacketType` members | 82 declarations (80 live plus `ERROR`/`UNKNOWN`) |
| Inbound-attributed (`[Packet(...)]`) | 24 (11 Auth, 13 World) |
| Contract-project LOC | 3,148 across 95 files |

Duplicate opcodes: **none** — every `PacketType` value is unique, so a C++ `switch` dispatch table is well-formed.

The three shared projects deliberately target `netstandard2.1` so a .NET client could consume them, but **the Unity client never did**. `C:\dev\3D\Assets\Plugins\` contains exactly two DLLs — `protobuf-net.dll` and `BouncyCastle.Cryptography.dll` — no `Avalon.*` assembly, no submodule, no package reference; `Avalon.Client.Network.asmdef` has `"references": []`. Every packet is hand-mirrored with a namespace rewrite, and the commit log says so in as many words (*"add SUnitDeathPacket **client mirror**"*).

The drift that produced is measurable: the client carries **5 opcodes the server has retired or never had** (`CMSG_ATTACK = 0x2100`, `CMSG_ENTER_MAP = 0x2021`, `CMSG_TARGET_UNIT = 0x2102`, `SMSG_MAP_TELEPORT = 0x3030`, `SMSG_MAP_TRANSITION = 0x3031`), and the server has **32 packet classes with no client mirror at all**. A C++ client written the same way would be a third copy of the same definitions, and the failure mode is silent: mismatched field numbers deserialize into the wrong properties rather than throwing.

### 1.2 The prose documentation cannot substitute for a schema

`docs/networking-packet-protocol.md` is the document a client author reads first, and it is wrong on most of its technical specifics: it describes `Flags` as "Encryption, Compression, Reserved" when the code has `None/ClearText/Handshake/Encrypted`; it describes `Protocol` as logical channel grouping when it is a dead transport selector (`Invalid/Tcp/Udp/Both`, read by nothing); it says `Version` is available for parallel handlers when it is hardcoded `0` at every construction site; and its framing description ("fixed field lengths; header marshaled first enabling preallocation") describes something the wire has never done — the header is a variable-length protobuf message behind a Base128 varint length prefix. Its extensibility recipe is wrong twice over: identification is by a `public static NetworkPacketType PacketType` **field** found by reflection (`PacketReader.cs:45-46`), not the `[Packet]` attribute's `Type` property, and it omits the second registration path entirely, so following it for a gameplay packet produces a handler that is never invoked.

A generated schema plus a generated opcode table replaces the parts of that document that a client actually needs, and cannot drift without a red build.

### 1.3 Why now

The window is open exactly once. The only other client is being deleted, and it is the only thing a wire break would break. Every wire-format decision this spec defers becomes expensive the day a second live client exists.

---

## 2. Ownership stays with C#: generate `.proto` *from* the contracts

The obvious reading of "shared source of truth" is to author `.proto` and generate C# from it. **This spec deliberately chooses the cheaper direction: generate `.proto` from the existing C# contracts and check it in, guarded by a test that fails when they drift.**

### 2.1 What inverting ownership would actually cost

`protoc --csharp_out` emits `Google.Protobuf` types — `IMessage`, `MessageParser`, `RepeatedField<T>`, `ByteString`. That is not a rename; it is a serializer replacement underneath the most heavily optimised subsystem in the repository:

| What | Fate under inversion |
|---|---|
| **80 `public static NetworkPacket Create(...)` factories** across 75 of 95 files | Deleted. Generated classes cannot carry them; they must be re-homed to extension methods or a builder layer. |
| **3 static wire fields per class** (`PacketType`/`Protocol`/`Flags`) | Deleted. These *are* the dispatch mechanism (`PacketReader.cs:45`, `PacketManager.cs:33`); both reflection-built registries must be rewritten against a new table. |
| `: Packet` marker base | Gone. Every `where T : Packet` constraint needs a new one. |
| The protobuf-net runtime | Replaced throughout `PacketSerializationHelper`, `OutboxSerializer`, `InboundPacketFrame`, `PacketReader`, `NetworkPacketDeserializer`, `PooledArrayBufferWriter`. |
| Namespaces and type names | **184 files** across `src/`+`tests/`+`tools/` carry `using Avalon.Network.Packets…`; **102** file-level references name specific packet classes. |

The hidden cost is the last row of that list rather than any of the visible ones. `docs/gc-pressure.md` tracks GC-001..GC-013+, nearly all of them in this exact code: pooled buffer writers, the zero-copy `InboundPacketFrame`, delegate caches, the channel and tick-driven outboxes, burst batching. `Google.Protobuf`'s allocation profile differs, and the `ReadOnlyMemory<byte>` zero-copy slicing that `InboundPacketFrame` depends on becomes `ByteString`. The baseline that work established is now recorded (§2.2); inverting ownership puts all of it back on the bench for no benefit that the export direction does not already deliver.

### 2.2 The serialization baseline that a swap would perturb

Recorded 2026-09-10 into `docs/benchmarks.md` (commit `045dc6a5`), BenchmarkDotNet 0.15.8, .NET 10.0.12, i9-12900K:

| Method | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---|---|---|---|
| `Serialize_NoEncryption` | 205.5 ns | 1.28 ns | 1.07 ns | 0.0556 | 872 B |
| `Serialize_Encrypted` | 951.8 ns | 17.08 ns | 15.98 ns | 0.1364 | 2,144 B |
| `Deserialize_Encrypted` | 527.0 ns | 10.06 ns | 12.36 ns | 0.1106 | 1,744 B |
| `Deserialize_NoEncryption` | 182.0 ns | 3.03 ns | 2.83 ns | 0.0131 | 208 B |

Those figures did not exist before that commit, and the reason they did not is worth remembering: the suite's `GlobalSetup` built the server side with `GenerateECDHKeyPair(128)` (secp128r1) and handed the public key to an `AvalonCryptoSession` defaulting to P-256, so `ECDHBasicAgreement.CalculateAgreement` threw — and a stray `[SimpleJob(RuntimeMoniker.Net80)]` on a `net10.0` project turned that crash into the message *"There are not any results runs"*, which reads like an empty filter. Production uses P-256 on both sides (`CryptoManager.cs:19`, `IAvalonCryptoSession.cs:42`, `AsymmetricCipher.cs:38`); the fix matched the benchmark to it and renamed the mislabelled `*_Aes128` scenarios. **A job-level build failure reporting as "no results" is a masking pattern worth recognising anywhere it recurs.**

### 2.3 The honest caveat

This is **export, not shared ownership**. A C++-side need — say, a field the client wants — still has to be expressed as a C# edit and re-exported. Given that the server is the protocol's only author and the only other client is being deleted, that hierarchy is correct anyway. It should be stated in the generated file's header comment so nobody edits the checked-in `.proto` by hand.

---

## 3. Evidence: the generator already works

Everything in this section was run, not inferred — protobuf-net's schema generator against the built `bin/Release/netstandard2.1` assemblies, and `protoc 32.0` against its output.

### 3.1 The generator runs clean over all 93 contracts

`RuntimeTypeModel.Default.GetSchema(SchemaGenerationOptions)` over all 93 `[ProtoContract]` types:

```
PROTO3 OK. lines=595 chars=13821
PROTO2 OK. lines=595
```

No exception and no warning, in either syntax: **595 lines, 13,821 characters, 93 messages, 13 enums, 231 fields.** A freshness check ran first — the built DLL exposes exactly 231 `[ProtoMember]` properties, matching source — so the schema reflects HEAD rather than a stale binary.

None of the constructs that normally make an attributed C# contract set un-exportable are present. Counts are zero, not low: zero `[ProtoInclude]` or subtype hierarchies, zero `AsReference`, zero surrogates, zero `DynamicType`, zero custom serializers, zero `RuntimeTypeModel` configuration, zero `decimal`, zero `TimeSpan`, zero `object`/`Type` members, zero `DataFormat` overrides, zero arguments on any `[ProtoContract]`. The 81 `: Packet` declarations are not serialization inheritance — `Packet.cs` is an empty class with no `[ProtoContract]`, invisible to protobuf-net and absent from the generated schema, existing only to satisfy `where T : Packet` constraints.

### 3.2 The one thing that stops it compiling: enum value scoping

`protoc 32.0` rejects the emitted proto3 with **7 errors, all of one cause**:

```
all_proto3.proto:183:4: "Success" is already defined.
  Note that enum values use C++ scoping rules, meaning that enum values are siblings of
  their type, not children of it.  Therefore, "Success" must be unique within the global
  scope, not just within "MFAOperationResult".
```

`Success` is declared in **7** of the 13 enums and `InternalError` in **2**, verified in source:

```
Auth/MFAOperationResult.cs:5           Success,
Auth/SLogoutPacket.cs:24               Success,          :28  InternalError,
Auth/SWorldSelectPacket.cs:9           Success = 0,
Character/SCharacterCreatedPacket.cs:25  Success,
Character/SCharacterDeletedPacket.cs:24  Success = 0,    :26  InternalError = 2,
Handshake/SServerInfoPacket.cs:9       Success = 0,
World/SMapTransitionPacket.cs:9        Success = 0,
```

The generator emits one flat file with no `package` declaration, so every value lands in the global scope. **The fix is wire-neutral and additive:** protobuf-net 3.4.21 ships `[ProtoEnum(Name = "…")]`, whose own documentation states that the name *"is not used during serialization"*. Roughly **9 attributes on 9 enum members** rename them in the schema only — no C# call site changes, no wire change, no behaviour change. `SchemaGenerationOptions.Package` alone does **not** fix it, because three of the colliding `Success` values are already in the same `Auth` namespace.

Proof the cause is singular: mechanically prefixing every enum value with its enum name and recompiling produced `fixed_proto3.pb.cc` and `fixed_proto3.pb.h` first try, with no other diagnostic.

### 3.3 The five nullable scalars, where `null` and zero differ on the wire

Six fields use nullable value types, and the semantics are load-bearing — `TargetGuid = null` means *clear target*, `KillerGuid = null` means *environmental death*:

| Field | Type | Location |
|---|---|---|
| `CCastAbilityPacket.TargetGuid` | `ulong?` | `Abilities/CCastAbilityPacket.cs:18` |
| `CCastAbilityPacket.GroundPos` | `Vector3Dto?` | `Abilities/CCastAbilityPacket.cs:19` |
| `SWorldHandshakePacket.AccountId` | `long?` | `Auth/SWorldHandshakePacket.cs:14` |
| `CTargetUnitPacket.TargetGuid` | `ulong?` | `Combat/CTargetUnitPacket.cs:26` |
| `SCharacterDamagePacket.AbilityId` | `uint?` | `Combat/SCharacterDamagePacket.cs:18` |
| `SUnitDeathPacket.KillerGuid` | `ulong?` | `Combat/SUnitDeathPacket.cs:18` |

`GroundPos` is a message, so proto3 gives it presence for free. **Five scalars are the problem.** Presence is real on the wire and protobuf-net transmits it — measured on `CTargetUnitPacket`:

```
TargetGuid = null : []          (nothing written)
TargetGuid = 0    : [08 00]     (field written, explicitly zero)
TargetGuid = 7    : [08 07]
```

protobuf-net's proto3 emitter flattens these to bare `uint64 TargetGuid = 1;`, and a `protoc`-generated reader then re-encodes `0` as *absent* — presence lost, silently, and only on a value that is rare in testing. Adding the single word `optional` restores it: `protoc --decode` of the `08 00` bytes reports `TargetGuid: 0`, re-encoding is byte-identical, an empty input decodes to absent, and C++ gains `has_targetguid()`.

Proto2 emission would give every field presence for free, but it also decorates non-nullable scalars with `[default = 0]` and generates a C++ API with `has_x()` on everything. **proto3 plus explicit `optional` on the five named fields is the choice.**

> **Superseded, and the criterion above is the part that did not survive.** C# nullability turns out not to predict which members lose a value to a reader without explicit presence. protobuf-net decides whether to write a `string` or `bytes` member by testing the reference against null, so an empty one goes out as a present field of length zero that plain proto3 reads as the default and writes back as nothing; and a `ReadOnlyMemory<byte>` member cannot be null at all, so the server writes one even where nothing was assigned. Neither is visible to a nullability rule, and both are reachable in production. `optional` is therefore applied wherever the server can write a value plain proto3 reads as absent — every singular `string` and `bytes` member, and every nullable scalar. 54 fields carry the keyword now, 49 more than the five named here. Repeated members cannot take it and do not need it; singular message members have presence already. `schema/README.md` holds the rule as it stands; this section is kept as the decision that was taken on the day, not as a description of the schema.

### 3.4 The five fields that serialise through non-standard extensions

The only genuine protobuf-net extension usage in the entire contract set:

| Type | Field | Location |
|---|---|---|
| `DateTime` | `CChatMessagePacket.DateTime` (tag 2) | `Social/CChatMessagePacket.cs:17` |
| `DateTime` | `SChatMessagePacket.DateTime` (tag 5) | `Social/SChatMessagePacket.cs:18` |
| `Guid` | `MapInfo.InstanceId` (tag 2) | `Character/SCharacterSelectedPacket.cs:27` |
| `Guid` | `SChunkLayoutPacket.InstanceId` (tag 2) | `World/SChunkLayoutPacket.cs:45` |
| `Guid` | `SMapTransitionPacket.InstanceId` (tag 2) | `World/SMapTransitionPacket.cs:25` |

The generator emits `import "protobuf-net/bcl.proto";` and types them `.bcl.DateTime` / `.bcl.Guid`. These are **not** the well-known types anyone would guess. Measured encoding of `2026-09-10T12:00:00Z`: `2a 06 08 f8 d4 3c 10 01` — a submessage of `{sint64 value = 496956, scale = HOURS}`, a scaled delta from the Unix epoch, not a `google.protobuf.Timestamp`. `bcl.Guid` is two `fixed64`s in .NET's byte order, not 16 bytes in RFC order.

Cost: `bcl.proto` is ~30 lines, is **not shipped in the NuGet package**, and must be vendored from the protobuf-net repository; then the C++ side needs roughly 40 lines of conversion (`bcl::DateTime` → `std::chrono::sys_time`, `bcl::Guid` → a 16-byte array), because `protoc` gives you the struct and nothing more.

Both `DateTime`s are chat timestamps and all three `Guid`s are map-instance ids, so the usage looks incidental in origin. Changing them to `int64` unix-millis and `bytes` would delete the dependency entirely at the cost of a wire break — **which is nearly free during this window and will not be later.** This spec treats it as a decision to be taken in Phase 1 rather than a given, and notes one unknown: whether anything depends on `Guid` specifically, such as a database column type, was not established.

### 3.5 The verification harness is `protoc` itself

No C++ compilation, no second protobuf runtime, no bespoke differential rig: feed `protoc` the bytes protobuf-net produced and the candidate schema. A fully-populated `CharacterInfo` (all 11 fields, non-default values, 50 bytes) decoded correctly and re-encoded **byte-identical**. Four further cases:

| Case | Result |
|---|---|
| Envelope `NetworkPacket` | byte-identical |
| `bcl.DateTime` (`SChatMessagePacket`) | byte-identical |
| `repeated uint64`, unpacked (`SInstanceStateRemovePacket`) | byte-identical |
| `ulong? = 0` (`CTargetUnitPacket`) | **diverges** — `08 00` vs empty; byte-identical after `optional` |

One divergence, found and fixed in about ten minutes of probing. That is the shape of the whole verification job.

Two notes that ride along. The envelope needs no special handling — `NetworkPacket { header = 1, payload = 2 }` is ordinary protobuf, and `InboundPacketFrame`'s hand-rolled fast parse is an optimisation *of* that encoding, forward-compatible because it skips unknown fields (`InboundPacketFrame.cs:44-58`). And the generator emits `repeated uint64 Removes = 1 [packed = false];`, which matches what protobuf-net writes; readers on both sides accept either encoding per spec, so this is a note rather than a blocker — **provided the annotation is never dropped by a hand-edit of the generated file.**

### 3.6 What the schema cannot carry: opcodes and flags

Opcode identity is not stored anywhere as data. It is derived at runtime by reflecting a `public static NetworkPacketType PacketType` field off each attributed class (`PacketReader.cs:45-46`), through two independent registries — the DI/async route (`ServiceCollectionExtensions.cs:31-48`, `:60-82`) and a second attribute-keyed tick-thread route for World gameplay (`WorldServer.cs:168-172`). There is no artifact anywhere that says "0x2000 ↔ `CAuthPacket`", and a C++ client cannot reflect.

The same is true of `Flags` — whether a given packet class is `Encrypted`, `ClearText` or `None` is a static field per class (measured distribution: `Encrypted` 67, `ClearText` 6, `None` 1, absent 7), and a client must know per opcode whether to encrypt.

**Therefore the generator emits a side-car table** — `schema/opcodes.json` carrying `{opcode, hex, message name, flags, direction}` — from the same reflection the server already performs, executed at build time instead of startup. Roughly 30 lines of C#. The alternative, custom protobuf message options, is more elegant and self-describing but protobuf-net's emitter will not produce it, so it becomes a post-processing step that buys the C# side nothing.

Two fields must **not** be built on: `Header.Protocol` is read by nothing, and `Header.Version` is hardcoded `0` at every construction site.

---

## 4. The entity-field blob: replace it, do not port it

This is the sharpest part of the work, and the part a schema project can most easily leave undone while appearing complete.

### 4.1 What it is

`SMSG_WORLD_STATE_ADD` and `SMSG_WORLD_STATE_UPDATE` — entity replication, the highest-frequency server→client traffic — carry their payload as an opaque `bytes` member:

```csharp
[ProtoContract] public class ObjectAdd {
    [ProtoMember(1)] public ulong Guid { get; set; }
    [ProtoMember(2)] public ReadOnlyMemory<byte> Fields { get; set; }
}
```

`Fields` is not protobuf. It is written by `src/Server/Avalon.World/Serialization/WorldObjectWriter.cs` (156 lines), a `BinaryWriter` subclass. Precisely: **a 4-byte little-endian bitmask header, followed by up to 13 presence-gated fixed-width fields in a fixed order, one value-dependent nested conditional, then type-dependent unconditional trailers including a .NET 7-bit-length-prefixed UTF-8 string — with two of the five object layouts writing no header at all.**

Four properties make it hostile to reimplement:

**The bitmask is not zero-based.** `GameEntityFields` declares `None = 1 << 0` through `IsDead = 1 << 15`. It is written as an `int32` (`WorldObjectWriter.cs:35` and `:87`), always 4 bytes, whether or not any field is set. Two of its sixteen bits are decorative: `CreatureMetadataId` (bit 11) and `Name` (bit 12) are declared and included in the `All` mask, but no writer branch tests them — both values are written unconditionally.

**One conditional is value-dependent, not bit-driven** (`WorldObjectWriter.cs:123-138`):

```csharp
if (fields.HasFlag(GameEntityFields.PowerType))
{
    Write((byte)worldObject.PowerType);
    if (worldObject.PowerType != PowerType.None)      // reads a VALUE, not a bit
    {
        if (fields.HasFlag(GameEntityFields.Power))        { Write(worldObject.Power!.Value); }
        if (fields.HasFlag(GameEntityFields.CurrentPower)) { Write(worldObject.CurrentPower!.Value); }
    }
}
```

So `Power` and `CurrentPower` can have their bits set in the header and still not be on the wire. The bitmask is not a sufficient description of the payload.

**There are five type-dependent layouts and the discriminator is not in the blob.** It is the top byte of the enclosing `Guid` (`ObjectGuid.cs:21-23`, `TypeShift = 56`). From the decoder's own header comment: Character ADD/UPDATE and Creature ADD/UPDATE carry a header; **SpellProjectile ADD carries none** (three `Vector3` floats, raw); SpellProjectile UPDATE carries one; **Portal ADD carries none** (position, radius, target map id, role); Portal UPDATE is never sent. A decoder must dispatch on the guid's type byte *and* on whether the packet was an ADD or an UPDATE before it knows whether byte 0 is a header or a float.

**One byte exists solely to keep a foreign file aligned** (`WorldObjectWriter.cs:146-154`):

> *"Single byte: 1 = dead, 0 = alive. Read in the same position by the client `EntityFieldDecoder.DecodeUnit`. Only Character gates today; if the worldObject is a Creature (no IsDead), we still write 0 to keep wire alignment correct — the client decoder mirror reads this byte position whenever the IsDead bit is set in the fields header."*

A creature has no `IsDead` concept and the server writes padding for it anyway, to stay byte-aligned with a file in a repository that is about to be deleted. That comment is the drift risk stated in literal form.

### 4.2 How it has behaved

`git log --follow` gives eight commits over the encoder's life, and the shape matters more than the count. Born 2024-07-18 (`5e757169`, partial update delta packets), churned through 2024-10-20, then **untouched for eighteen months**, then three format-touching commits in five days in April 2026 (`e787eb90` Portal layout on 2026-04-24; `98faaabb` the `IsDead` bit and `e144980d` the `IsDead` byte on 2026-04-28), then untouched again for the four and a half months since.

**Both 2026 changes broke the wire.** `e144980d` inserted a byte into the middle of the unit layout, immediately before the unconditional `Name` in the character tail — a decoder not updated in lockstep reads the `IsDead` byte as `Name`'s length prefix and desynchronises the remainder of the stream. `e787eb90` added a fifth layout with no header, which an unaware client cannot parse at all.

The honest characterisation is neither "changes monthly" nor "frozen": **long dormancy punctuated by short bursts of breaking change, most recently four and a half months ago, with the two most recent edits both breaking the wire.** The dormancy tracks the feature set being static, not the format being finished — the burst coincided with portals and death arriving. More gameplay means more bursts.

And nothing would catch a third break. **The format has zero test coverage of any kind** — grepping `tests/` for `WorldObjectWriter`, `BinaryReader` or `WorldObjectReader` returns nothing but build artefacts — and **there is no server-side decoder**: nothing in this repository can read what it writes. The only decoder ever written is 134 lines of C# in the Unity client, now preserved in this repository at **`docs/reference/EntityFieldDecoder.cs.txt`**, alongside a copy of the encoder at `docs/reference/WorldObjectWriter.cs.txt`.

### 4.3 The size argument does not survive measurement

The one real reason to keep a hand-tuned format is that it is smaller than the general-purpose alternative. It is not. Both sides were measured with the same tool — `protoc 32.0` — with the blob's length modelled branch-for-branch from `WorldObjectWriter` and then validated against real protobuf output (a 79-byte blob encodes to a 91-byte body, 93 as a repeated element, matching the model exactly). Comparison is full per-entity wire cost, because that is what ships. Sample values: a level-42 character with a 9-character name, a creature with an 8-character name.

Variants: **A** = nested `Vec3` message; **B** = `repeated float [packed]`; **C** = A with the guid split into `type` + `id`.

| Scenario | blob | blob as element | A (nested) | B (packed) | C (split guid) |
|---|---:|---:|---:|---:|---:|
| Character UPDATE (13 fields + name) | 79 | **93** | 83 | 87 | 78 |
| Character UPDATE, self-suppressed | 51 | **65** | 54 | 54 | 49 |
| Creature UPDATE (7 fields + name) | 55 | **69** | 65 | 69 | 61 |
| Character ADD (`All` mask) | 79 | **93** | 83 | 87 | 78 |
| Creature ADD (`All` mask) | 66 | **80** | 73 | 77 | 69 |
| SpellProjectile UPDATE, sparse | 16 | **30** | 24 | 26 | 18 |
| SpellProjectile UPDATE, dense | 32 | **46** | 41 | 45 | 35 |
| SpellProjectile ADD (blob's best case — no header) | 28 | **42** | 41 | 45 | 35 |

**Proto3 is smaller in eight of eight scenarios under the natural design (variant A), by 1 to 11 bytes, and by 7 to 16 with the guid split.** There is no crossover, for three compounding reasons: the blob pays six bytes of pure overhead per entity that proto3 does not (the always-on 4-byte header plus the 2-byte tag and length of the opaque `bytes` wrapper); it spends fixed-width 64-bit integers on `Experience` and `RequiredExperience` where varints typically cost 4–5, and a `uint16` on a `Level` bounded near 100; and proto3 omits zero-valued scalars inside `Vec3`, which a fixed 12-byte triple cannot.

Sensitivity, bounded for Character UPDATE (blob = 93 bytes), since variant A's margin depends on how many vector components are exactly zero:

| Zero components | A | B | C |
|---|---:|---:|---:|
| 4 (stationary, velocity = 0,0,0) | 73 (−20) | 87 (−6) | 68 (−25) |
| 2 (pos.y = 0, vel.y = 0) | 83 (−10) | 87 (−6) | 78 (−15) |
| 1 (vel.y = 0) | 88 (−5) | 87 (−6) | 83 (−10) |
| 0 (worst case for A) | 93 (±0) | 87 (−6) | 88 (−5) |

**The worst case for the natural design is exact break-even, never a loss**, and the best case — a stationary entity, which in an ARPG is most creatures most of the time — is the largest win. Variant B is zero-insensitive at a flat −6 if predictability is preferred over best case.

### 4.4 Replacing deletes more code than it adds

The blast radius is two files. `WorldObjectWriter.cs` (156 lines, deleted outright) and `MapInstance.cs`, whose **eight call sites all sit inside one method**, `BroadcastStateTo` (`:303`–`~:460`): the four add paths at `:349`, `:354`, `:360`, `:366`, and the update paths at `:407-408` (character), `:413` (creature) and `:419` (projectile). Nothing else in `src/` or `tests/` touches the format. The one benchmark that looks like a consumer, `BroadcastStateGcBenchmarks`, uses a synthetic `new byte[64]` stand-in and never calls the writer.

It does not reach into game logic. The writer only *reads* `IWorldObject` / `IUnit` / `ICharacter` / `ICreature` / `PortalInstance` properties; no gameplay type changes and the dirty-tracking machinery is untouched. **`GameEntityFields` stays exactly as it is** — it is doing two jobs, and only the wire job is being replaced; it remains the server's internal dirty-tracking vocabulary.

`BroadcastStateTo` has its current shape only because the blob is opaque bytes that must be placed by hand into a scratch buffer. Replacing it removes, in round numbers, **sixty lines of buffer plumbing** — and two documented hazards go with them:

- The rented 64 KB scratch buffer (`:312` `ArrayPool<byte>.Shared.Rent(65536)`, returned at `:459`) and the manual offset/length slicing at `:341`, `:374`, `:400`, `:429`.
- **Two capacity guards whose documented failure mode is client corruption** (`:326`, `:389`). The comment at `:333-337`: *"A skipped add will NOT be retried … The client will receive subsequent delta updates for an entity it has never seen, producing a corrupt state."*
- **A use-after-return lifetime hazard held together by a comment** (`:375`, `:430`): *"NOTE: Fields slice is valid only until `ArrayPool.Shared.Return(buffer)` in the finally block. `PacketSerializationHelper.Serialize` … is synchronous, so the slice is consumed before Return is reached."* Correct today; silently a use-after-free the moment anything on that path becomes asynchronous. With real messages the serializer owns the memory and the hazard cannot be written.

### 4.5 The message shape

The mapping is natural rather than forced, because proto3 explicit presence encodes exactly and only what the bitmask encodes:

```proto
message Vec3 { float x = 1; float y = 2; float z = 3; }

message ObjectState {
  uint64 guid                          = 1;   // see the guid-encoding follow-up in §9
  optional Vec3      position          = 2;
  optional Vec3      velocity          = 3;
  optional float     orientation       = 4;   // yaw only — the blob transmits Orientation.y
  optional MoveState move_state        = 5;
  optional uint32    health            = 6;
  optional uint32    current_health    = 7;
  optional PowerType power_type        = 8;
  optional uint32    power             = 9;
  optional uint32    current_power     = 10;
  optional uint32    level             = 11;
  optional bool      is_dead           = 12;
  optional uint64    experience        = 13;
  optional uint64    required_experience = 14;
  optional uint32    creature_metadata_id = 15;
  optional string    name              = 16;
}
```

Every awkward part of the format disappears rather than being translated. The 13 gated fields become 13 `optional`s. The value-dependent `PowerType != None` nesting is gone — simply do not set `power`/`current_power` when the unit has no power type. The `IsDead` creature-padding byte is gone, because there is no offset to keep aligned. The two decorative bits stop being lies. The five type-dependent layouts collapse to one message, which is what makes the headerless-ADD special case possible today. And .NET's 7-bit-length-prefixed string becomes protobuf's varint-prefixed string — the same encoding by coincidence, but the client stops needing a .NET-specific framing rule. Portal's `radius`/`target_map_id`/`role` join as further `optional`s or take their own message; either is unremarkable.

### 4.6 The risk, and the mitigation that is also the sequencing constraint

This is a wire break executed with **no reference decoder and no existing tests**. If the new messages get a semantic detail wrong — which entity types set which fields, whether `orientation` is really only yaw — nothing catches it, and the Unity client will no longer be around to compare against.

**Land it while `docs/reference/EntityFieldDecoder.cs.txt` is still the authority, and write C# round-trip tests against the generated types before `WorldObjectWriter.cs` is deleted.** That gives the format its first test coverage in its two-year life, which is worth having whichever way the decision had gone.

For contrast, the option not taken: porting the decoder to C++ requires hand-implementing a non-zero-based 16-bit flag enum, .NET `BinaryWriter` little-endian primitives, .NET 7-bit-length-prefixed UTF-8 strings, `ObjectGuid` bit-unpacking to learn the layout, five type-dependent layouts of which two have no header, the ADD-vs-UPDATE distinction as a *parsing* input, the value-dependent `PowerType` nesting and the `IsDead` padding rule — roughly 200 lines of C++ that must stay byte-aligned with a C# file by review alone, forever, with no schema and no tests on either side. It is also the only option with a deadline attached to the deletion of another repository.

---

## 5. The C++ side

### 5.1 The build integration was probed, not assumed

The live worry was that `vulkan-multithreaded` declares `cmake_minimum_required(VERSION 4.0)` and builds with CMake 4.2, which rejects projects declaring below 3.5 — and a from-source protobuf drags in Abseil. **It does not materialise.** Audited across the whole tree:

```
protobuf v31.1:            1x 2.8.12   2x 3.16   1x 3.26
abseil-cpp 20250127.0:                 2x 3.16
```

The single `2.8.12` is `examples/CMakeLists.txt`, guarded by `protobuf_BUILD_EXAMPLES` which is OFF by default and never processed. Protobuf's own root declares the range form `3.16...3.26`, unchanged across v31.1, v33.6 and v36.1 — a wide, stable window, not a knife-edge. No pin bump was needed; the first version tried worked.

The probe configured, built and ran both runtimes, with the presence contract asserted explicitly (unset → absent, `set(0)` → **present**, `clear_` → absent) and **both binaries emitting exactly 39 bytes** for the same message — the wire-compatibility claim measured rather than quoted.

### 5.2 Build cost, and the reason to generate out-of-band

Cold, on the probe machine, plain `cmake --build` with no `--parallel`:

| Phase | Full config (protoc built) | Client config (protoc OFF) |
|---|---:|---:|
| Cold configure incl. clone | 42 s | 66 s (download variance) |
| **Cold build, Debug** | **663 s (11 min 03 s)** | **182 s** |
| Cold build, Release | — | 179 s |
| Warm reconfigure | 5 s | — |
| Incremental after a one-line edit | 10 s | — |
| Projects generated | 117 | 114 |

**Roughly three quarters of the eleven minutes is `protoc` and `libprotoc` — code that generates code, which a client neither links nor needs to rebuild.** What remains is about three minutes, almost all of it Abseil (which is ~92 of the projects either way), and it is a one-time cost on a clean checkout of the kind that repository already pays for GLFW, FreeType, zstd, lz4 and imgui.

**Therefore: generate the C++ sources once with a prebuilt `protoc` and check the output in.** That removes the compiler half of the cost entirely, and takes the `protobuf_BUILD_LIBUPB` trap (§6) off the table with it.

### 5.3 Runtime size, and the lite/full choice

Release, which is what ships:

| Executable | Bytes | Delta vs baseline |
|---|---:|---:|
| baseline (no protobuf) | 15,360 | — |
| **lite** | 278,016 | **+257 KB** |
| **full** | 1,330,688 | **+1.25 MB** |

In Debug the same deltas are +4.9 MB and +10.2 MB. The static libraries are large on disk — `libprotobuf.lib` 78,616,100 bytes Release against `libprotobuf-lite.lib` 9,212,508, plus ~20 MB of Abseil; Debug figures are 194 MB and 27 MB with `libprotocd.lib` alone at 474 MB — but the linker discards most of it, so those are a disk cost rather than a shipping one.

**Size is not the reason to choose lite; build time and dependency surface are, and even those are modest.** `libprotobuf-lite` is byte-identical on the wire, keeps every generated accessor including `has_`/`clear_`, and keeps explicit presence. What it drops is descriptors and reflection, and with them `DebugString()` — no free human-readable dump of a packet in a log or an inspector panel. If a network-inspector editor panel is on that engine's roadmap, weigh that first: it is descriptor-driven and cannot be added back without moving the whole schema to the full runtime.

One detail that must be settled here rather than there: `optimize_for = LITE_RUNTIME` is a **file-level option in the `.proto` itself**, so it has to be present in the shared schema. Verified: it does not affect the C# generator's output — the C# runtime has no lite/full split and ignores the option — so adding the line is a no-op for the server and a win for the client. Also verified: `protoc` v31.1 accepts it in a **proto3** file, contrary to some older documentation.

Alternatives that clear the bar of *consumes standard `.proto`, emits standard wire bytes*: **nanopb** (real option if size ever dominates — ~10 kB runtime class, but plain C structs, compile-time size caps or callbacks for strings/bytes/repeated, and a Python dependency in the content build) and **protobuf-c** (less compelling at both ends). **upb** should not be depended on directly — upstream is explicit that its API and ABI are not stable. FlatBuffers, Cap'n Proto and MessagePack define their own wire formats and cannot talk to a C# protobuf server at all; protozero has no message codegen and so does not deliver the goal.

### 5.4 Where it lives in that repository

A new sibling static library `net`, above `common`, mirroring the existing `assetpak` shape: `target_link_libraries(net PUBLIC common PRIVATE protobuf::libprotobuf-lite)`. The `PRIVATE` containment holds only if generated message types never appear in `net`'s public headers — confine `.pb.h` to `net/src/*.cpp` and expose hand-written PODs. That is not an imposition: the ECS components there are already PODs crossing a lock-free seam, and the repository has fought the header-weight fight once already, over a header-only JSON library. `common` is the wrong home — it is linked PUBLIC by four targets including a deliberately imgui-free runtime.

Two conventions from that repository to match: the varint framing (§5.5) and the parse entry point should follow its existing binary-format signature style, `parseMessage(bytes, out) -> bool`, over `std::vector<std::byte>`; and its house error contract is exactly right for untrusted socket bytes — every existing codec returns `false` or empty on malformed input and lets the caller log and degrade, never throwing and never asserting.

### 5.5 What is hand-written on the C++ side regardless

**The frame is not describable in a `.proto`.** Layer 1 is a 1–5 byte Base128 varint length prefix (write side `OutboxSerializer.cs:11-31`, read side `PacketStream.cs:55-102`); layer 2 is the ordinary protobuf `NetworkPacket`. Roughly 25 lines of C++, on both sides, by necessity. Add the generated opcode `switch` (§3.6) and ~40 lines of `bcl` conversion (§3.4) if those five fields survive Phase 1.

---

## 6. Traps

Each of these fails quietly. The consequence column is what happens if it is missed.

| Trap | Consequence if missed |
|---|---|
| **`protobuf_MSVC_STATIC_RUNTIME` defaults ON.** It is a `cmake_dependent_option` on `NOT protobuf_BUILD_SHARED_LIBS`, and the engine already forces `BUILD_SHARED_LIBS OFF` (for lz4) — which is precisely the condition that activates the default. Protobuf then sets `CMAKE_MSVC_RUNTIME_LIBRARY` to `/MT` `/MTd` **in its own directory scope**, so it does not conflict at the CMake level at all. | Protobuf and all 92 Abseil targets compile `/MTd` against the engine's `/MDd`. Because protobuf's interface is full of `std::string`, this corrupts across the boundary rather than producing a clean LNK2038. **Set `protobuf_MSVC_STATIC_RUNTIME OFF` before `FetchContent_MakeAvailable`**, and verify it landed in the generated `.vcxproj` files rather than assuming — the probe confirmed zero `/MT` or `/MTd` across all 117. |
| **`protobuf_BUILD_LIBUPB=OFF` breaks the compiler build.** It is not an independent switch: `libprotoc` includes upb's bootstrap header, whose `descriptor.upb.h` is generated only when `libupb` builds. | `error C1083: Cannot open include file: 'google/protobuf/descriptor.upb.h'` — **after roughly three minutes of building**. libupb must be ON whenever protoc is built, and may only be OFF when it is not. Generating out-of-band (§5.2) removes the choice. |
| **The five `bcl` fields (§3.4).** `DateTime` is a scaled epoch delta, not `google.protobuf.Timestamp`; `Guid` is two `fixed64`s in .NET byte order, not RFC order. `bcl.proto` is not in the NuGet package. | A C++ client that assumes the well-known types reads plausible-looking wrong timestamps and byte-swapped GUIDs, with no error anywhere. |
| **The five nullable scalars (§3.3).** protobuf-net's proto3 emitter drops `optional`. | Presence is lost silently, and only when the value equals its type default — a zero id. `TargetGuid = 0` and *clear target* become indistinguishable, in code that will pass every test written with non-zero fixtures. |
| **The enum value collision (§3.2).** proto3 enum values are siblings of their type in the global scope. | The generated schema does not compile at all — 7 errors. This one is loud, and it is the only one on this list that is. Fixed by ~9 `[ProtoEnum(Name = …)]` attributes, which change the schema name only and never the wire. |

Four secondary ones, cheap to state and expensive to rediscover:

- **`protobuf_BUILD_EXAMPLES` must stay OFF.** It is the only path to the `cmake_minimum_required(VERSION 2.8.12)` file, which *is* a hard CMake 4.0 error. Do not flip it to get a sample.
- **`protobuf_WITH_ZLIB` defaults ON** and will fetch zlib if it is not found — a third repository, for a gzip stream feature nothing here uses. Turn it off.
- **Offline/CI mirrors need two source overrides, not one.** Abseil is protobuf's own nested `FetchContent_Declare`, so overriding only `FETCHCONTENT_SOURCE_DIR_PROTOBUF` still reaches the network for Abseil — and that is discovered only when the network is gone.
- **`[packed = false]` on `repeated uint64` is load-bearing** in the checked-in schema (§3.5). It matches what protobuf-net writes; both readers accept either encoding, but the annotation must not be lost to a hand-edit.

---

## 7. Phases

Two to three weeks in total. Each phase is independently landable, and Phase 2 is the least certain estimate in the plan.

### Phase 1 — Schema generation and the drift guard — 1.5–2.5 days

A console project under `tools/` (`Avalon.Benchmarking` and `Avalon.ChunkImporter` are the precedent) referencing the two contract projects: call `RuntimeTypeModel.Default.GetSchema(...)`, apply the two post-processing rules — `optional` on the five named scalars, and the `[ProtoEnum(Name = …)]` renames applied as source attributes — and write `schema/avalon.proto` plus `schema/opcodes.json`. Add `optimize_for = LITE_RUNTIME`. Split the emission per domain group (Auth / Character / Combat / State / Social / World) rather than one flat file: `protoc` generated **2.5 MB of C++** from the 93-message schema as a single translation unit (`fixed_proto3.pb.cc` 1,338,851 bytes, `fixed_proto3.pb.h` 1,196,613 bytes), which is a real compile-time item downstream.

Decide, and record in the file header, whether the five `bcl` fields stay or become `int64` unix-millis and `bytes`. This is the phase where a wire break is nearly free.

Then a test that regenerates and asserts the checked-in files are unchanged. **Schema drift becomes a red build**, which is the property the whole exercise exists to buy.

### Phase 2 — Round-trip verification and a golden corpus — 3–5 days — *least certain*

Property-based generation over reflection: fill every one of the 93 types with randomised non-default values and — critically — *deliberately* with defaults and nulls, because those are the dangerous cases. Round-trip each through protobuf-net → bytes → `protoc --decode` → `--encode` → `cmp`, and the reverse direction into protobuf-net `Deserialize` for a field-by-field compare. Freeze the results as a checked-in corpus of `.bin` plus expected-text pairs, so the C++ side can test against fixed vectors without needing .NET.

**The uncertainty is real and it is why this phase is the loosest estimate.** There is nothing to build on. The suite is 96 files and 717 cases (716 executed, 1 skipped) on xUnit 2.9.3 and NSubstitute 6.2.0 with raw `Assert.*`, and a grep across all of `src/` and `tests/` for `AutoFixture|Bogus|Faker|FluentAssertions|Shouldly|Verify.Xunit|ApprovalTests|Moq` returns **zero hits**. There are no `[MemberData]` or `[ClassData]` attributes anywhere, no object mothers, no shared packet fixtures, and no golden vectors of any kind. Of the 93 contract types, **27 are referenced by any test and only 22 are ever constructed as populated instances**; the entire server→client broadcast surface — every `SUnit*`, every `SCharacter*`, and all of `State/` — is untested. No test anywhere asserts the exact serialized bytes of a protobuf message, and none deserializes a hardcoded byte array.

Two things are worth reusing rather than rebuilding. `PacketStreamShould.cs:18-23` holds the only checked-in byte vector in the repository — three varint-framed frames, asserted byte-exact — directly usable as a framing-layer conformance vector for C++. And `InboundPacketFrameShould.cs` is already a differential test in miniature: it serializes with protobuf-net and parses with `InboundPacketFrame.ParseFrame`, an independent hand-rolled wire parser. That is the template to scale up.

One thing to avoid: the three outbox tests use `SPingPacket.Create(0L, 0L, 0L, 0L)` as filler, and since all four fields are `long` and protobuf omits default scalars, **that call produces an empty payload**. As a differential vector it carries no information at all.

Hand-authoring 93 fixtures against a suite that today populates 22 types is the single largest available cost sink in this project, and reflection-driven generation is what avoids it.

### Phase 3 — Entity state messages replace the blob — 3–5 days

Add `ObjectState` (§4.5) and the Portal fields, rewrite the eight `BroadcastStateTo` call sites against real messages, delete `WorldObjectWriter.cs` and the buffer plumbing around it, and keep `GameEntityFields` as internal dirty-tracking vocabulary.

**Two ordering constraints, both hard.** Phase 2 lands first: round-trip tests must exist before the encoder changes, because the encoder is the thing being changed and there is nothing else to check it against. And this phase must land **while `docs/reference/EntityFieldDecoder.cs.txt` is still the authority** on what the bytes mean — that is the only surviving statement of the format's semantics, and once the questions it answers stop being answerable, they stop being answerable permanently.

The prior scope estimate put a port at +2–3 days and a replacement at +1–2 weeks. Having read every call site, 1–2 weeks reads high — the blast radius is one method in one file with no tests to migrate — but the schema-design and round-trip-test work is real, so the estimate here sits between them.

### Phase 4 — Protobuf in the C++ build, sources checked in — 1–2 days

`FetchContent` protobuf pinned at v31.1 with the option set from §5.1, letting protobuf fetch its own Abseil at the version its `dependencies.cmake` pins rather than declaring it ourselves. **Generate the `.pb.cc`/`.pb.h` once with a prebuilt `protoc` and check the output in**, rather than building the compiler in every clean checkout — that is the 663 s → 182 s difference, and it also removes the `libupb` trap. Create the `net` static library and confine `.pb.h` to its `src/`.

One cosmetic note that costs an eleven-minute rebuild to discover: `full_name()` returns a `string_view`, not a `std::string`, in v31.1.

Note the solution-file consequence: the probe generated 117 `.vcxproj` files, 92 of them Abseil, against an engine tree that currently holds 180. That takes the solution to roughly 300 projects, and the engine's ThirdParty solution-folder loop names targets individually, so it needs a directory-property sweep to stop them landing in the solution root.

### Phase 5 — C++ envelope: framing, dispatch, `bcl` conversion — 2–3 days

The varint frame reader and writer (~25 lines each side), the opcode dispatch table generated from `opcodes.json` — treating `NetworkPacketFlags` as an integer rather than an enum, since it is a `[Flags]` type whose composite values have no proto names — and the `bcl` conversion helpers if those five fields survived Phase 1.

---

## 8. What remains unknown

Stated plainly, because an inaccurate scope is worse than an incomplete one.

**The fixture strategy is the least certain part of the estimate.** Phase 2 is built from nothing (§7), and its 3–5 day range is the widest in the plan. What would settle it: build the reflection-driven generator for a single domain group first — Combat, which contains four of the six nullable fields — and measure how much of the 93-type surface it covers unattended before per-type intervention is needed. That converts the rest of the estimate from a guess into a multiplication.

**Real traffic volume was never established, and cannot be from the repository.** There is no telemetry and no packet capture, and no measured wire or payload byte counts exist anywhere in either repository. The only in-repo signal on entity counts is the benchmark author's own model, `BroadcastStateGcBenchmarks.cs:27`, `[Params(5, 20)] public int EntityCount`. Taking that at face value against the measured 10 Hz broadcast interval (`MapInstance.cs:31`, `BroadcastInterval = 0.1f`) and roughly 10 bytes saved per entity gives ≈2 KB/s per player and ≈200 KB/s server-wide at 100 concurrent players — small in absolute terms, and a saving rather than a cost, but **an extrapolation, not a measurement.** What would settle it: histogram `length` at `MapInstance.cs:374` and `:429`, where the blob length is already computed, alongside `AddedObjects.Count` and `UpdatedObjects.Count` over a representative session; the size table in §4.3 then converts directly into bandwidth. Per the project rule against instrumenting shipped code, that belongs in a test harness driving `MapInstance`, not in the server. A socket-level capture would do as well.

**Whether the five `bcl` fields are negotiable** is a judgement, not a finding. They are two chat timestamps and three map-instance ids, which *looks* incidental, but whether anything depends on `Guid` specifically — a database column type, for instance — was not checked.

**Whether `protoc --decode`'s text round-trip is byte-exact for float edge cases** (NaN, ±inf, denormals, −0.0) was not probed; the probes used ordinary values, and text-format float round-tripping is the one place the harness itself might be lossy. Include those values in the Phase 2 generator to find out. **This is a limitation of the `--decode`/`--encode` harness, not of the schema** — if it bites, the fallback is a small C++ test binary linking the generated code, which is byte-exact by construction.

**Not every packet was confirmed reachable by exactly one dispatch table.** Both registration paths were read but not exhaustively cross-checked; at least one dead entry is known (`CRegisterPacket` carries `[Packet]` but no handler implements `IAuthPacketHandler<CRegisterPacket>`, so `PacketManager` logs *"does not have a handler"* and drops it at startup). Not a schema problem, but it affects what a C++ client should bother implementing.

Finally, the current dormancy cuts both ways and should not be over-read. The last substantive commits in both repositories are from 2026-05-08; everything since is dependency automation. **The protocol is stable through four months of inactivity, not because anyone declared it finished.** Binding to it now is safe from a churn standpoint; assuming it is complete is not.

---

## 9. Out of scope

**TLS on the World port (21001), and the QUIC question.** These are separate decisions that follow this one, and neither has ever been decided by this project — `docs/architecture-decisions.md` holds five ADRs and none touches transport, serialization or crypto. The relevant findings are recorded in `docs/security-review-network-crypto.md` (commit `b368e3c4`) and the cipher baseline in `docs/benchmarks.md` (commit `045dc6a5`), and they are deliberately not folded in here. Two figures are worth carrying so they survive: World gameplay traffic **is** encrypted today in both directions, so TLS there would replace cost rather than add it — the current path costs roughly 1 µs per packet against ~0.19–0.22 µs for platform AES-GCM, an indicative ~5×, of which about 480 ns per call is call shape (a fresh `Init` on a shared cipher under a lock, plus LINQ allocations) rather than cipher throughput. Measured across three payload sizes, `System.Security.Cryptography.AesGcm` is 3.0–3.6× faster on encrypt and 1.9–2.9× on decrypt than the current BouncyCastle call shape, allocating 120–1,080 B against 1.7–4.6 KB per encrypt and **zero** against 1.5–2.5 KB per decrypt. The schema work is independent of all of it.

**The dirty-mask finding.** This is a real bandwidth issue and it is a different change. `EntityTrackingSystem` computes genuine per-entity dirty masks and carries them to `CharacterGameState.UpdatedObjects` as `(Guid, Fields)` — and the broadcast path then **discards them** and writes a compile-time constant mask instead: `MapInstance.cs:407-408` uses `MaskSelfSuppression(GameEntityFields.CharacterUpdate, …)` and `:413` uses `GameEntityFields.CreatureUpdate`. Only `SpellProjectile` (`:419`) uses the real mask. So every character update ships all 13 fields and every creature update all 7, ten times a second, whether or not anything changed — an idle character still transmits position, velocity, experience and required-experience. The only genuine narrowing is `MaskSelfSuppression` (`:573-577`), which strips `Position | Velocity | Orientation` from a player's own character.

Two consequences follow, and both belong here rather than in §4. First, the size comparison in §4.3 is measured against the traffic that actually exists: the dense rows *are* the common case, and the sparse scenario the format was designed for barely occurs. Second, **real per-field delta encoding is a larger win than either option in §4** — a true dirty mask on a mostly-idle entity would cut a 93-byte update to roughly 20. Presence-based messages make that a one-line change at the call site; the blob makes it an audit of every `HasFlag` branch against a hand-written decoder. It is a reason the replacement is the right base to build on, and it is **not** part of this slice.

**Other follow-ups recorded, not folded in:**

- **The broadcast path has no test coverage.** `MapInstance.BroadcastStateTo` and the two methods that describe an entity for it are untested, and the entity-state tests one layer down choose their own field selections, so swapping a selection in `MapInstance` or dropping its self-suppression call leaves every test green. It is the same code the dirty-mask finding above would change, so the two are worth picking up together. Recorded with its consequences under *Test Coverage* in `docs/instanced-maps.md`.
- **`ObjectGuid` costs 9–10 bytes as a varint on every entity.** `RawValue = ((ulong)type << 56) | id` (`ObjectGuid.cs:21-22`) makes every non-empty guid ≥ 2⁵⁶, so protobuf-net's default varint spends 10 bytes on it — more than `Position`, about 11% of a character update. Both options in §4 pay it identically, so it cancels out of that comparison, but `fixed64` (9 bytes) or splitting into `uint32 type` + `uint64 id` (~5 bytes) is worth doing regardless. **The natural pull is to fix this in the same change; it should not ride along uncontrolled.**
- **`NetworkPacketHeader.Size => 2 + 2 + 2 + 4`** (`NetworkPacket.cs:30`) is a constant that never matches the wire, and it feeds `InboundPacketFrame.Size` → `BytesReceivedCount` → the OpenTelemetry counters, so every network byte metric is systematically wrong.
- **The client outbound pump writes and flushes per packet** where the server coalesces a whole tick into one write. On the Unity client that is `Connection.cs:207-215`; the same shape should not be reproduced in the C++ client.
- **`docs/networking-packet-protocol.md` needs rewriting or retiring** once the generated schema and opcode table exist (§1.2).

---

## 10. Verification

- **Phase 1:** the drift-guard test is the deliverable — regenerate, assert unchanged, fail the build otherwise. Plus `protoc` compiling the checked-in schema clean, which is what the enum renames buy.
- **Phase 2:** the round-trip corpus, green in both directions, with defaults and nulls deliberately represented.
- **Phase 3:** C# round-trip tests over the generated entity-state types, written **before** `WorldObjectWriter.cs` is deleted. `dotnet build -c Release` with no new warnings against the pre-existing baseline, and `dotnet test -c Release` at 716 passing.
- **Phase 4:** the engine configures and builds clean, and the generated `.vcxproj` files are checked for `MultiThreadedDebugDLL` — the trap in §6 is silent, so it must be verified rather than assumed.
- **Phase 5:** the framing vector from `PacketStreamShould.cs:18-23` decodes byte-exact in C++, and the Phase 2 corpus parses.

# Reference copies

Source files preserved here as `.txt` because they document a wire format that exists nowhere
else in writing. They are not compiled and are not maintained. Read them as evidence of what
the bytes looked like, not as code to use.

## The entity-field blob — retired

`SMSG_WORLD_STATE_ADD` and `SMSG_WORLD_STATE_UPDATE` used to carry a `Fields` payload that was
**not protobuf**. Inside a protobuf message it was an opaque `bytes` member; its contents were a
hand-rolled little-endian format with a bitmask deciding which fields were present, and .NET's
7-bit-length-prefixed strings.

**It is no longer sent.** Both packets carry `ObjectState` — a real message, every field
carrying its own presence — and nothing produces or consumes these bytes any more. Nothing here
needs porting to a new client. The files are kept because they are the only record of a format
that shipped for two years, and because a question about what an old capture contained can still
be asked.

Two halves:

- **`WorldObjectWriter.cs.txt`** — the encoder, as it stood at
  `src/Server/Avalon.World/Serialization/WorldObjectWriter.cs` before that file was deleted.
- **`EntityFieldDecoder.cs.txt`** — the decoder, copied from a client that has since been
  retired. It was the only decoder for this format in existence; nothing on the server side
  ever read it, and no test covered it.

### Where the two disagree

They were never held against each other while both were live, and they do not agree. Two field
widths differ, and each difference desynchronises everything after it in the payload rather than
producing one wrong value — the encoder is the authority on what actually went out.

| Field | Encoder writes | Decoder reads |
|---|---|---|
| Creature metadata id | 8 bytes | 4 bytes |
| Portal target map id | 2 bytes | 4 bytes |

With the decoder's widths, a creature payload takes the top half of the identifier for the
length prefix of the name that follows, and a portal payload takes the role byte as part of the
map id and then runs off the end. So the decoder is a correct statement of *which* fields are
present and in what order — which is what it is worth reading for — and not of how wide two of
them are.

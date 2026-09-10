# Reference copies

Source files preserved here as `.txt` because they document a wire format that exists nowhere else in
writing. They are not compiled and are not maintained. Read them as evidence of what the bytes look
like, not as code to use.

## The entity-field blob

`SMSG_WORLD_STATE_ADD` and `SMSG_WORLD_STATE_UPDATE` carry a `Fields` payload that is **not protobuf**.
Inside a protobuf message it is an opaque `bytes` member; its contents are a hand-rolled little-endian
format with a bitmask deciding which fields are present, and .NET's 7-bit-length-prefixed strings.

Two halves, and until now only one of them lived in this repository:

- **`WorldObjectWriter.cs.txt`** — the encoder, copied from
  `src/Server/Avalon.World/Serialization/WorldObjectWriter.cs`. The live version is authoritative;
  this copy exists so the pair can be read side by side.
- **`EntityFieldDecoder.cs.txt`** — the decoder, copied from the Unity client at
  `Assets/Scripts/GameServices/Services/EntityFieldDecoder.cs`. **That client is being retired, and this
  was the only decoder for this format in existence.** Nothing in this repository decodes the blob, and
  no test covers it.

Any future client has to reimplement the decoder from these two files. Schema-generation work does not
cover it: a generated schema describes the envelope around the blob and says nothing about its contents,
which is precisely the part most likely to drift, because the encoder can change without any schema
noticing.

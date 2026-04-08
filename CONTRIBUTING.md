# Contributing

## Getting Started

1. Fork the repository and create a feature branch.
2. Run tests before opening a PR: `dotnet test`
3. Run benchmarks if touching simulation hot paths: `dotnet run -c Release --project tools/Avalon.Benchmarking`
4. Keep new abstractions in `*.Public` projects if they are shared across server boundaries.
5. Update docs for any structural change.

## Extending the Domain

### Add a new `ValueObject<T>`

1. Create a class deriving from `ValueObject<TPrimitive>` in `src/Shared/Avalon.Common`.
2. Add validation rules in the constructor or factory method.
3. OpenAPI schema and JSON serialization are handled automatically — no extra registration needed.

### Add a packet handler

**Auth server** (`src/Server/Avalon.Server.Auth/Handlers/`):
1. Define the packet contract in `Avalon.Network.Packets` with a new `NetworkPacketType` enum value.
2. Implement `IAuthPacketHandler<TPacket>`.
3. Register in DI — Auth handlers are resolved manually from the container.

**World server** (`src/Server/Avalon.Server.World/Handlers/`):
1. Define the packet contract in `Avalon.Network.Packets` with a new `NetworkPacketType` enum value.
2. Implement `IWorldPacketHandler<TPacket>`, decorated with `[PacketHandler(NetworkPacketType.X)]`.
3. No manual registration needed — handlers are discovered via reflection scan in the `WorldServer` constructor.

See [Networking — Packet Protocol](docs/networking-packet-protocol.md) for the full protocol reference.

### Add a world script

1. Define the contract in `src/Server/Avalon.World.Scripts.Abstractions`.
2. Implement in `src/Server/Avalon.World.Scripts`.
3. Register via the DI extension method in `Avalon.World`'s `ServiceExtensions`.

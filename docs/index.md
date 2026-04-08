---
hide:
  - navigation
---

# Avalon Server

Official server-side solution for the Avalon MMORPG: API, authentication, world simulation, networking, persistence,
telemetry, and extensibility frameworks.

For a full project overview, see the [README](https://github.com/WoozChucky/Avalon.Server#readme) on GitHub.

## Components

| Component | Role |
|---|---|
| **REST API** | HTTPS/JWT, account management, OpenAPI |
| **Auth Server** | TCP login flow, MFA, world-key issuance |
| **World Server** | Game simulation loop, packet dispatch, world lifecycle |
| **Core World** | Instanced maps, entities, spell/creature systems, chat |

## Quick Links

- [Configuration Reference](configuration-reference.md)
- [Architecture Overview](architecture-startup-flow.md)
- [Packet Protocol](networking-packet-protocol.md)
- [Contributing](contributing.md)

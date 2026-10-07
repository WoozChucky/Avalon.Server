---
hide:
  - navigation
---

# Avalon Server

Server-side solution for the Avalon MMORPG: the REST API, the TCP auth and world servers, world simulation,
networking, persistence, telemetry and tooling.

For a project overview, see the [README](https://github.com/WoozChucky/Avalon.Server#readme) on GitHub.

## Components

| Component | Role |
|---|---|
| **REST API** | HTTPS/JWT accounts, MFA, tokens, client auth and game admission, commerce, world content administration, public tooltips; OpenAPI |
| **Auth Server** | TCP login flow, MFA, world list and select, world-key issuance |
| **World Server** | Tick loop, connections, packet dispatch, world lifecycle |
| **Core World** | Instances, creatures and AI, abilities and combat, parties, quests, auras, items, chat |

## Where to start

- [Development Setup](development-setup.md) and the [Configuration Reference](configuration-reference.md)
- [World Simulation](world-simulation.md), [Instanced Maps](instanced-maps.md), [Map Generation](map-generation.md)
- [Auth Server](auth-server.md) and [REST API Authentication](api-authentication.md)
- [Packet Protocol](networking-packet-protocol.md) and [Packet Handlers](packet-handlers.md)
- Gameplay: [Parties](parties.md), [Quests](quests.md), [Auras](auras.md), [Inventory and Saves](inventory-and-saves.md), [Item Use](item-use.md), [Chat and Commands](chat-and-commands.md)
- [Contributing](contributing.md)

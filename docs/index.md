---
hide:
  - navigation
---

# Avalon Server

Server-side solution for the Avalon MMORPG: the REST API and TCP world servers, world simulation,
networking, persistence, telemetry and tooling.

For a project overview, see the [README](https://github.com/WoozChucky/Avalon.Server#readme) on GitHub.

## Components

| Component | Role |
|---|---|
| **REST API** | HTTPS/JWT accounts, MFA, tokens, client auth and game admission, commerce, world content administration, public tooltips; OpenAPI. Four services from one binary ([API Services](api-services.md)) |
| **Identity API** | REST sign-in, MFA, game contexts and world join-ticket admission |
| **World Server** | Tick loop, connections, packet dispatch, world lifecycle |
| **Core World** | Instances, creatures and AI, abilities and combat, parties, quests, auras, items, chat |

## Where to start

- [Development Setup](development-setup.md) and the [Configuration Reference](configuration-reference.md)
- [World Simulation](world-simulation.md), [Instanced Maps](instanced-maps.md), [Map Generation](map-generation.md)
- [API Services](api-services.md), [REST API Authentication](api-authentication.md) and the [World Player Limit](world-player-limit.md)
- [Packet Protocol](networking-packet-protocol.md) and [Packet Handlers](packet-handlers.md)
- Gameplay: [Parties](parties.md), [Trading](trading.md), [Quests](quests.md), [Auras](auras.md), [Inventory and Saves](inventory-and-saves.md), [Item Modifiers](item-modifiers.md), [Item Use](item-use.md), [Chat and Commands](chat-and-commands.md)
- [Contributing](contributing.md)

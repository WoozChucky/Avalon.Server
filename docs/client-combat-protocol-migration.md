# Client Combat Protocol Migration (V1)

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** V1 (2026-05-07). Updated alongside future combat protocol changes.

This document describes how the client must change to align with the server's V1 combat protocol. It is the canonical client-integration reference; future combat protocol revisions amend this document in place.

## 1. Conceptual Changes

- Auto-attack is **removed**. Basic attack is an ordinary ability (one per class, see §10), fired per click via `CCastAbilityPacket`. There is no separate "attack" opcode any more.
- A 200 ms hidden GCD lives on the server. The client should **not** pre-gate casts; on rejection the server returns `SAbilityNotReadyPacket` carrying the remaining time.
- Exit-paths (map transitions in V1; future fast-travel / waypoint / town-portal) work mid-combat. The server zeroes the player's threat across all hostiles in the encounter as the player leaves.
- Combat tag (`IsInCombat`) is unchanged on the wire — it flows via the existing per-character state stream. Client renders the combat icon as before.
- Threat HUD: a client whose currently-targeted unit is a hostile creature in an encounter receives `SThreatListPacket` updates, throttled to ~250 ms per (connection, target) pair and additionally suppressed when the top-attacker share moved by less than 5 %.
- New persistent target packet `CTargetUnitPacket`: the client tells the server which unit it currently targets. The server stores it on the connection and uses it to scope `SThreatListPacket` broadcasts.

## 2. Removed Packets

| Packet | Replacement |
|---|---|
| `CCharacterAttackPacket` (`CMSG_ATTACK`, was `0x2100`) | `CCastAbilityPacket` (`CMSG_CAST_ABILITY`, `0x2101`) |

The client must DELETE its emitter for `CCharacterAttackPacket`. Every attack action — including basic attack — now emits `CCastAbilityPacket` with the appropriate ability id. The old enum value `0x2100` is retired and explicitly marked as such in `NetworkPacketType.cs`; do not reuse it.

## 3. Renamed Packets and Fields

The on-the-wire numeric tags (`NetworkPacketType` enum values and `[ProtoMember(N)]` field numbers) are preserved. Only the C# names changed.

| Old | New | Wire-tag preserved |
|---|---|---|
| `SCharacterSpellsPacket` | `SCharacterAbilitiesPacket` | yes — same `SMSG_CHARACTER_ABILITIES = 0x3027` |
| `SSpellNotReadyPacket` | `SAbilityNotReadyPacket` | yes — same `SMSG_ABILITY_NOT_READY = 0x3104` |
| `SpellInfo` (sub-message) | `AbilityInfo` | yes — all `[ProtoMember(N)]` numbers identical |
| `SpellId` field (any) | `AbilityId` | yes |
| `SCharacterDamagePacket.SpellId` | `SCharacterDamagePacket.AbilityId` | yes — `[ProtoMember(5)]` unchanged |

If the client uses generated proto types from the shared schema, recompiling against the latest schema is sufficient. If the client hand-rolls deserialization, only the C# property names need updating — the wire layout is byte-identical.

Note: the cast-interruption enum constant has been renamed to `SMSG_INTERRUPTED_CAST` (numeric value `0x3105` preserved for wire compatibility). The corresponding C# class is `SCharacterInterruptedCastPacket`.

## 4. New Packets

### `CCastAbilityPacket` (`CMSG_CAST_ABILITY = 0x2101`, encrypted, TCP)

Client → server. Sent on every ability click (basic attack included). Fire-and-forget; rejections come back as `SAbilityNotReadyPacket`.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `AbilityId` | 1 | `uint` | Ability id from the player's ability list (`SCharacterAbilitiesPacket.Abilities[].AbilityId`). |
| `TargetGuid` | 2 | `ulong?` | Raw `ObjectGuid` of the target unit. `null` for self / ground / AoE abilities. |
| `GroundPos` | 3 | `Vector3Dto?` | Reserved for V2 ground-target abilities. V1 unused — set `null`. |

**Emission rule.** One packet per click. Do not pre-gate on cooldown, GCD, range, facing, cost, or combat-state. The server validates everything and replies with `SAbilityNotReadyPacket` on rejection.

### `CTargetUnitPacket` (`CMSG_TARGET_UNIT = 0x2102`, encrypted, TCP)

Client → server. Sent when the player changes their current target. The server stores `TargetGuid` on `IWorldConnection.CurrentTargetGuid` and uses it to decide which encounter's threat list (if any) to broadcast back via `SThreatListPacket`.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `TargetGuid` | 1 | `ulong?` | Raw `ObjectGuid` of the targeted unit, or `null` to clear the current target. |

**Emission rule.** Send on target acquisition and on target clear. There is no need to resend on every frame; the server keeps the value sticky until the next `CTargetUnitPacket` arrives.

### `SUnitDeathPacket` (`SMSG_UNIT_DEATH = 0x3107`, encrypted, TCP)

Server → all clients in the instance. Triggers death animation + state transition.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `UnitGuid` | 1 | `ulong` | Raw `ObjectGuid` of the unit that died. |
| `KillerGuid` | 2 | `ulong?` | Raw `ObjectGuid` of the killer, or `null` for environmental / unattributed kills. |

`SUnitDamagePacket` already carries the final hit (HP reaching 0). `SUnitDeathPacket` is the explicit death signal — use it to trigger the death animation, ragdoll, and (for the local player) the Release UI.

### `SUnitRevivePacket` (`SMSG_UNIT_REVIVE = 0x3108`, encrypted, TCP)

Server → all clients in the instance. Triggers revive animation, snaps position, and resets HP.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `UnitGuid` | 1 | `ulong` | Raw `ObjectGuid` of the revived unit. |
| `Position` | 2 | `Vector3Dto` | World-space position to snap to. |
| `Health` | 3 | `uint` | Current HP after revive. |

**V1 caveat (own character).** The current "die → release → home town" path uses the existing `World.ApplyDeathLogoutAsync` flow, which persists town coordinates and full HP at logout time. Because `IsDead` and `CurrentHealth` are not persisted, on re-login the character is alive at full HP without an `SUnitRevivePacket`. The packet is fully wired and is used for in-session revives (and reserved for V2 party-revive / corpse-walk / partial-HP penalty).

### `SThreatListPacket` (`SMSG_THREAT_LIST = 0x3109`, encrypted, TCP)

Server → single client. Sent only to the connection whose `CurrentTargetGuid` (set via `CTargetUnitPacket`) is the hostile creature in question, AND only when the top-attacker's threat share moved by more than ~5 %, AND throttled to ~250 ms per (connection, target) pair.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `TargetGuid` | 1 | `ulong` | Raw `ObjectGuid` of the hostile creature. |
| `Entries` | 2 | `ThreatEntry[]` | Per-attacker entries; see below. |

Each `ThreatEntry`:

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `AttackerGuid` | 1 | `ulong` | Raw `ObjectGuid` of an attacker on this hostile's threat list. |
| `ThreatPercent` | 2 | `float` | Share of total threat in `[0.0, 1.0]`. |

## 5. Cast Pipeline Expectations (Client Side)

- Click → emit `CCastAbilityPacket {AbilityId, TargetGuid?, GroundPos?}`. The server validates everything; the client must NOT pre-gate.
- On rejection, the server replies with `SAbilityNotReadyPacket {AbilityId, CooldownMs (uint)}`. Trigger reasons: GCD, per-ability cooldown, cost shortfall, combat-state mismatch (`RequiresOutOfCombat` / `RequiresInCombat`), out of range, not facing, dead. Use the `CooldownMs` field (remaining cooldown in milliseconds) to drive HUD feedback. Other rejection reasons (cost, range, combat-state) send `CooldownMs = 0`.
- For abilities with `CastTime > 0`, the server replies `SUnitStartCastPacket` (existing, generic). Render the cast bar from the `CastTime` field on the packet.
- On completion, the server replies `SUnitFinishCastPacket` (existing, generic). End the cast bar and play the cast-finish animation.
- On movement-interrupt, the server replies `SCharacterInterruptedCastPacket {Caster, AbilityId}` (existing). Power refund is handled server-side; the client just ends the cast bar.

The client never emits a separate "interrupt" or "cancel" packet — moving cancels in-progress casts implicitly via existing movement state.

## 6. Threat HUD

- Send `CTargetUnitPacket` on target acquisition / change / clear. Without it, the server will not send `SThreatListPacket` for any target.
- Subscribe to `SThreatListPacket`. Each packet is a complete snapshot of the threat list for the targeted hostile — replace, do not merge.
- Render `ThreatPercent` per attacker. Highlight the local player's row when they are top of the list (i.e. they have aggro on this target).
- The server already throttles broadcasts to ~250 ms; matching the UI redraw cadence to ~250 ms is sufficient — no client-side rate-limiting needed.
- Clear the HUD on `CTargetUnitPacket {TargetGuid = null}` and on target death (`SUnitDeathPacket` for the targeted unit).

## 7. Death and Revive UI

- `SUnitDeathPacket` for any unit → death animation, drop selection if this was the targeted unit. For the local player, additionally show the "Release" button.
- On Release, the existing logout-from-Normal-map flow returns the character to their home town. There is no new packet for this — the existing transition path runs.
- `SUnitRevivePacket` for any unit → revive animation, snap to `Position`, set local HP from `Health`. For the local player, hide the Release UI and re-enable input.

## 8. Removed Client-Side Concepts

- "Auto-attack" client state — toggle, cycle, queued-swing, attack-on-target. Delete all of it.
- Spell-vs-attack input split. Replace with a single "use ability" input mapped to `CCastAbilityPacket`. Hotbars now contain only abilities; the basic attack lives in slot 0 of the player's ability list (delivered via `SCharacterAbilitiesPacket` on character login).

## 9. Test Checklist (Client)

- [ ] Click on enemy → basic attack fires; server replies `SUnitDamagePacket` (and `SCharacterDamagePacket` for player damage).
- [ ] Spam click → server enforces GCD; sub-200 ms casts get `SAbilityNotReadyPacket` with non-zero `CooldownMs`.
- [ ] Cast a cast-time ability while moving → `SCharacterInterruptedCastPacket` arrives; cast bar clears.
- [ ] Get hit → combat icon visible (existing `IsInCombat` channel).
- [ ] Acquire a hostile target with `CTargetUnitPacket` → `SThreatListPacket` arrives once you have aggro; threat % updates throttled to ~250 ms.
- [ ] Switch target → `SThreatListPacket` for the previous target stops; the new target's list arrives.
- [ ] Clear target (`CTargetUnitPacket {null}`) → no further `SThreatListPacket` for the prior target.
- [ ] Die → `SUnitDeathPacket` arrives; Release button visible; on Release the character spawns at the home town.
- [ ] Map-transition / portal out mid-combat → no rejection; transition succeeds. Threat is zeroed server-side.
- [ ] Login → `SCharacterAbilitiesPacket` populates the ability list including the class basic attack.

## 10. Basic Attack Per Class

V1 seeds one basic-attack ability per class as ordinary `AbilityTemplate` rows in `WorldDbContext`. They are granted automatically on character creation (added to each class's `CharacterCreateInfos.StartingSpells`). The client retrieves the player's full ability list via `SCharacterAbilitiesPacket` on character login.

Seeded values (`Avalon.Database.World/Migrations/20260507161257_SeedBasicAttackAbilities.cs`):

| Class | AbilityId | Name | Range (`SpellRange`) | Damage | Cooldown | Cast Time | Threat × |
|---|---|---|---|---|---|---|---|
| Warrior | 100 | Warrior Slash | `Melee` (1 m) | 15 | 500 ms | 0 | 1.5 |
| Wizard | 101 | Wizard Bolt | `Medium` (10 m) | 8 | 700 ms | 200 ms | 1.0 |
| Hunter | 102 | Hunter Shot | `Long` (20 m) | 10 | 600 ms | 0 | 1.0 |
| Healer | 103 | Healer Wand | `Medium` (10 m) | 5 | 800 ms | 300 ms | 0.8 |

`Range` is the `SpellRange` enum (`Melee = 1`, `Short = 5`, `Medium = 10`, `Long = 20`); the underlying `ushort` value is the radius in metres. `Cooldown` and `CastTime` are in milliseconds. `Damage` and `Threat ×` are not transmitted in `AbilityInfo`; they are server-side only and surface on the client through `SUnitDamagePacket` / `SThreatListPacket` outcomes.

Numbers are V1 placeholders pending balance pass.

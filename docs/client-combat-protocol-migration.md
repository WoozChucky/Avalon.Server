# Client Combat Protocol Migration (V1)

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** V1 (2026-05-07). Updated alongside future combat protocol changes.

This document describes how the client must change to align with the server's V1 combat protocol. It is the canonical client-integration reference; future combat protocol revisions amend this document in place.

## 1. Conceptual Changes

- Auto-attack is **removed**. Every attack is an ordinary ability, fired per click via `CCastAbilityPacket`; each class starts with three skills (see §10). There is no separate "attack" opcode any more.
- A 200 ms hidden GCD lives on the server. The server validates every cast and answers every refusal with `SAbilityNotReadyPacket` carrying a reason (and, for the GCD and cooldowns, the remaining time).
- Casts aim, they do not target (#164). A cast aims along the caster's facing, or at the cursor's ground point for a Cursor skill, and the ability's shape decides who it affects. There is no range or facing check on a cast, and `TargetGuid` is ignored.
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
| `TargetGuid` | 2 | `ulong?` | Deprecated, ignored by the server (#164). Leave unset. |
| `GroundPos` | 3 | `Vector3Dto?` | The cursor's ground point. Required for `AbilityInfo.AimMode = Cursor`: missing or non-finite is refused as `NoAimPoint`. Height ignored. |

**Emission rule.** One packet per click. Do not pre-gate on cooldown, GCD, cost, or combat-state. There is no range or facing to pre-check: a cast aims rather than targets. The client may clamp its telegraph to `AbilityInfo.Reach`, since the server clamps the aim to it anyway. The server stays authoritative: it validates everything and answers every refusal with `SAbilityNotReadyPacket` carrying a `CastRejectReason`.

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

A creature's killing blow is sent like any other hit, before the death: `SUnitDamagePacket` with `CurrentHealth` 0, `Damage` the health the blow took, and `Result`, the hit's `HitResult` (a killing crit is marked `Crit`) (#506). A character's killing blow arrives the same way, as `SCharacterDamagePacket` to the character (with `AbilityId` and `Result`) and `SUnitDamagePacket` to its watchers. A dodged hit is sent as a hit of 0 marked `Dodged`. `SUnitDeathPacket` is the explicit death signal — use it to trigger the death animation, ragdoll, and (for the local player) the Release UI.

### `SUnitRevivePacket` (`SMSG_UNIT_REVIVE = 0x3108`, encrypted, TCP)

Server → all clients in the instance. Triggers revive animation, snaps position, and resets HP.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `UnitGuid` | 1 | `ulong` | Raw `ObjectGuid` of the revived unit. |
| `Position` | 2 | `Vector3Dto` | World-space position to snap to. |
| `Health` | 3 | `uint` | Current HP after revive. |

**V1 caveat (own character).** The current "die → release → home town" path uses the existing dead-logout flow in `World.DeSpawnPlayerAsync` (`ReviveForDeathLogout` on the tick, then `MoveToRespawnTownAsync` on the saved copy of the row), which persists town coordinates and full HP at logout time. Because `IsDead` and `CurrentHealth` are not persisted, on re-login the character is alive at full HP without an `SUnitRevivePacket`. The packet is fully wired and is used for in-session revives (and reserved for V2 party-revive / corpse-walk / partial-HP penalty).

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

### `SAbilityFiredPacket` (`SMSG_ABILITY_FIRED = 0x310A`, encrypted, TCP)

Server → all clients in the instance. A circle or cone skill fired (#164); draw it. Its hits arrive as the usual damage packets. Projectiles do not send it: they are world objects, replicated through the world-state add, update and remove packets.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `CasterGuid` | 1 | `ulong` | Raw `ObjectGuid` of the caster. |
| `AbilityId` | 2 | `uint` | The ability that fired. |
| `Origin` | 3 | `Vector3Dto` | The caster's position when it fired. |
| `Direction` | 4 | `Vector3Dto?` | A cone's direction, a unit vector on X/Z; absent for a circle. |
| `Centre` | 5 | `Vector3Dto?` | A circle's centre; absent for a cone. |

A circle sends `Centre`, a cone `Direction`.

### `CPvpTogglePacket` (`CMSG_PVP_TOGGLE = 0x2103`, encrypted, TCP)

Client → server. No fields. Toggles the player's PvP flag (#164), exactly as typing `/pvp` does: off turns on at once; on with no timer starts the off timer (`Game:PvpOffDelay`, default 5 minutes), during which the player stays hostile; on with a timer running cancels it and stays on. Always answered with `SPvpStatePacket`.

### `SPvpStatePacket` (`SMSG_PVP_STATE = 0x310B`, encrypted, TCP)

Server → the player's own client. The player's own flag and off timer.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `Enabled` | 1 | `bool` | Whether the flag is on. It stays on while the off timer runs. |
| `OffInMs` | 2 | `uint` | Time left on the off timer in milliseconds, rounded up; 0 when no timer is running. |

Sent in four cases: as the reply to every toggle (`CPvpTogglePacket` or `/pvp`), the moment the off timer turns the flag off (`Enabled = false`, `OffInMs = 0`), once when the character enters an instance, and when a hit moves a running timer. Every player-on-player hit restarts a running timer at its full length for both players, and the countdown is re-sent when a hit moves the deadline by more than a second: at most one packet per player per second of combat, and none while no timer runs. So the client can count `OffInMs` down locally, replace its countdown with each packet, and show it as exact.

**Other players' flags.** Every character state (`ObjectState`) carries `PvpEnabled` (field 21) only as `true`; absent on a character state means off. Towns never allow player hostility, whatever the flags.

## 5. Cast Pipeline Expectations (Client Side)

- Click → emit `CCastAbilityPacket {AbilityId, GroundPos?}` (`GroundPos` for a Cursor skill). The server validates everything and answers every refusal with a reason.
- On rejection, the server replies with exactly one `SAbilityNotReadyPacket {AbilityId, CooldownMs (uint), Reason (CastRejectReason)}` (#512). `Reason` names the refusal: `Gcd`, `Cooldown`, `RequiresOutOfCombat`, `RequiresInCombat`, `NotEnoughPower`, `Dead`, `NotOwned`, `InternalError`, `NoAimPoint` or `AlreadyCasting`; `Unknown` (0) is only what a payload without the field decodes as. `OutOfRange`, `TargetNotFound` and `NotFacing` remain in the enum but are no longer sent (#164). `CooldownMs` is the remaining time in milliseconds for `Gcd` and `Cooldown`, rounded up so it is always at least 1, and 0 for every other reason. The only cast that gets no answer is one from a connection with no character.
- A cast while another is in progress is refused as `AlreadyCasting`. A cast-time cast fires with the aim it started with: facing and ground point are captured when it starts.
- `AbilityInfo.FacingAngle` in `SMSG_CHARACTER_ABILITIES` is no longer set (#164): there is no facing cone.
- For abilities with `CastTime > 0`, every client in the instance receives `SUnitStartCastPacket` (existing, generic): render the cast bar from `CastTime`, and show which ability from `AbilityId` (#521 item 9).
- On completion, the server replies `SUnitFinishCastPacket` (existing, generic). End the cast bar and play the cast-finish animation.
- On movement-interrupt, the server replies `SCharacterInterruptedCastPacket {Caster, AbilityId}` (existing). Power spent on the cast is not refunded; the client just ends the cast bar.

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
- Spell-vs-attack input split. Replace with a single "use ability" input mapped to `CCastAbilityPacket`. Hotbars now contain only abilities: the ones listed in `SCharacterAbilitiesPacket`, delivered on character login. The list has no guaranteed order, so place skills by `AbilityId`, not by position.

## 9. Test Checklist (Client)

- [ ] Cast a cursor skill with a ground point → it fires; hits on creatures arrive as `SUnitDamagePacket` (and `SCharacterDamagePacket` for player damage, with `AbilityId` set).
- [ ] Cast a cursor skill without a ground point (or with a non-finite one) → `SAbilityNotReadyPacket` with `Reason = NoAimPoint`.
- [ ] Cast a circle or cone skill → every client in the instance gets `SMSG_ABILITY_FIRED` (a circle's `Centre`, a cone's `Direction`) and can draw the effect, whether or not it hit anyone.
- [ ] Cast a projectile skill → the projectile appears as a world object, flies, and is removed where it stopped (at `Reach`, at a wall, or at the first unit hit).
- [ ] Cast while a cast-time cast is in progress → `SAbilityNotReadyPacket` with `Reason = AlreadyCasting`.
- [ ] Spam click → server enforces GCD; sub-200 ms casts get `SAbilityNotReadyPacket` with non-zero `CooldownMs`.
- [ ] Cast a cast-time ability while moving → `SCharacterInterruptedCastPacket` arrives; cast bar clears.
- [ ] Get hit → combat icon visible (existing `IsInCombat` channel).
- [ ] Acquire a hostile target with `CTargetUnitPacket` → `SThreatListPacket` arrives once you have aggro; threat % updates throttled to ~250 ms.
- [ ] Switch target → `SThreatListPacket` for the previous target stops; the new target's list arrives.
- [ ] Clear target (`CTargetUnitPacket {null}`) → no further `SThreatListPacket` for the prior target.
- [ ] Die → `SUnitDeathPacket` arrives; Release button visible; on Release the character spawns at the home town.
- [ ] Map-transition / portal out mid-combat → no rejection; transition succeeds. Threat is zeroed server-side.
- [ ] Login → `SCharacterAbilitiesPacket` populates the ability list with the class's three starter-kit skills, each with its shape fields.

## 10. Starter Kit Per Class

Each class starts with three skills (#164), ordinary `AbilityTemplate` rows seeded by the World migration `SeedStarterSkillKit`. They are granted on character creation through each class's `CharacterCreateInfos.StartingSpells`, and the Character migration `GrantStarterSkillKit` gave every existing character its class's three in place of whatever it held. The client retrieves the player's full ability list via `SCharacterAbilitiesPacket` on character login, and learns each skill's shape from `AbilityInfo` fields 8-16 (`AimMode`, `Shape`, `Anchor`, `Reach`, `Radius`, `ArcDegrees`, `ProjectileSpeed`, `Pierce`, `Affects`), which is everything it needs to draw a telegraph.

The retired abilities 1, 2 and 100-103 are gone, and their ids are never reused, so a client that cached them cannot confuse them with a kit skill.

| id | class | name | shape | aim | anchor | reach (m) | radius (m) | arc (°) | speed (m/s) | pierce | affects | cast | cooldown | cost |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 200 | Warrior | Cleave | Cone | Movement | – | 2.5 | – | 100 | – | – | Hostile | 0 | 800 ms | 0 |
| 201 | Warrior | Ground Slam | Circle | Movement | Caster | 0 | 3 | – | – | – | Hostile | 0 | 5000 ms | 20 |
| 202 | Warrior | Hurled Axe | Projectile | Cursor | – | 15 | – | – | 18 | no | Hostile | 0 | 3000 ms | 10 |
| 210 | Wizard | Arcane Bolt | Projectile | Cursor | – | 20 | – | – | 22 | no | Hostile | 0 | 800 ms | 0 |
| 211 | Wizard | Flame Burst | Circle | Cursor | AimPoint | 18 | 3 | – | – | – | Hostile | 600 ms | 5000 ms | 25 |
| 212 | Wizard | Frost Fan | Cone | Cursor | – | 6 | – | 60 | – | – | Hostile | 0 | 4000 ms | 15 |
| 220 | Hunter | Quick Shot | Projectile | Cursor | – | 25 | – | – | 28 | no | Hostile | 0 | 800 ms | 0 |
| 221 | Hunter | Piercing Arrow | Projectile | Cursor | – | 30 | – | – | 24 | yes | Hostile | 0 | 4000 ms | 15 |
| 222 | Hunter | Scatter Shot | Cone | Cursor | – | 8 | – | 45 | – | – | Hostile | 0 | 4000 ms | 20 |
| 230 | Healer | Smite | Projectile | Cursor | – | 18 | – | – | 20 | no | Hostile | 0 | 800 ms | 0 |
| 231 | Healer | Radiant Pulse | Circle | Movement | Caster | 0 | 4 | – | – | – | Hostile | 0 | 5000 ms | 20 |
| 232 | Healer | Mending Circle | Circle | Cursor | AimPoint | 15 | 4 | – | – | – | Ally | 0 | 8000 ms | 25 |

On the wire `AbilityInfo.Cooldown` and `CastTime` are seconds (floats); the table gives the stored milliseconds. The cost is paid from the class's pool: Warrior Fury, Wizard and Healer Mana, Hunter Energy. Damage and heal amounts, and threat, are not transmitted in `AbilityInfo`; they are server-side only and surface through the damage packets, health replication and `SThreatListPacket`. `AbilityInfo.Range` still carries the legacy `SpellRange` value but the server no longer reads it; use `Reach`.

Numbers are placeholders pending a balance pass.

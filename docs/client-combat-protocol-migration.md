# Client Combat Protocol Migration (V1)

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** V1 (2026-05-07). Updated alongside future combat protocol changes.

This document describes how the client must change to align with the server's V1 combat protocol. It is the canonical client-integration reference; future combat protocol revisions amend this document in place.

## 1. Conceptual Changes

- Auto-attack is **removed**. Every attack is an ordinary ability, fired per click via `CCastAbilityPacket`; each class starts with three skills (see §10). There is no separate "attack" opcode any more.
- A 200 ms hidden GCD lives on the server. The server validates every cast and answers every refusal with `SAbilityNotReadyPacket` carrying a reason (and, for the GCD and cooldowns, the remaining time).
- Casts aim, they do not target (#164). A Movement skill aims from the caster toward the cursor when the cast sends `GroundPos`, else along the caster's facing (#716); a Cursor skill aims at the cursor's ground point; and the ability's shape decides who it affects. There is no range or facing check on a cast, and `TargetGuid` is ignored.
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
| `GroundPos` | 3 | `Vector3Dto?` | The cursor's ground point. Required for `AbilityInfo.AimMode = Cursor`: missing or non-finite is refused as `NoAimPoint`. Send it for `Movement` too (#716): the skill then points from the caster toward it (anchored on the caster, its reach still measured from the caster); without it, or with a non-finite one or one within a millimetre of the caster on X/Z, it points along the caster's facing, and it is never refused for it. Height ignored. |

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

**A corpse's world state (#672).** `ObjectState.IsDead` (field 12) is `true` on a dead creature as well as a dead character. A creature is dead exactly when its health is 0; the world-state update that carries its `CurrentHealth` 0 carries `IsDead = true` in the same message, and a client that first sees the corpse later (it walked into view, or entered the instance) gets `IsDead = true` on the add. A living creature's routine updates leave `IsDead` out, so absent on a creature update means unchanged, not alive. The corpse stays in the world-state stream until its body is removed (`BodyRemoveTimer`), then leaves with an ordinary remove. Creatures are never revived.

**A creature's rarity (#709).** `ObjectState.Rarity` (field 22, enum `CreatureRarity`: Normal 0, Elite 1, Rare 2, Boss 3) is on every state of a creature that is not Normal, the add and every update alike, whatever else changed, so a client can colour the nameplate from whichever message it sees first. Absent on a creature state means Normal. Characters, portals and projectiles never carry it. It is fixed when the creature spawns; a server-side template change reaches only creatures spawned after it.

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
| `CastId` | 6 | `uint` | The cast that fired (#648): its start's `CastId` for a cast-time cast, a fresh one for an instant cast. 0 from a server before #648. |
| `Footprint` | 7 | `AbilityFootprintDto?` | The whole footprint that fired, dimensions included (#648), so an instant ability is drawn without the caster's ability catalog. For a cast-time cast it is the footprint its start carried. |

A circle sends `Centre`, a cone `Direction`; both send `Footprint`, whose `Origin`, `Direction` and `Centre` are the same values.

### `AbilityFootprintDto` (#648)

Where an ability lands, as the server resolved it. The server resolves it once, from where the caster stood and what it aimed at when the cast started, and the cast fires with exactly this footprint, so a telegraph is never corrected. Every hit test is on X/Z.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `Shape` | 1 | `AbilityShape` | Circle, Cone or Projectile. |
| `Origin` | 2 | `Vector3Dto` | The caster's position when the cast started. Always sent. |
| `Direction` | 3 | `Vector3Dto?` | A cone's or a projectile's direction, a unit vector on X/Z; absent for a circle. |
| `Centre` | 4 | `Vector3Dto?` | A circle's centre: the origin (anchor Caster) or the aim point clamped to the reach and pulled back from any wall between (anchor AimPoint). Absent for the other shapes. |
| `Radius` | 5 | `float` | A circle's radius in metres; 0 otherwise. |
| `Reach` | 6 | `float` | A cone's length, or a projectile lane's up to where the walkable ray stops (the projectile may stop earlier on a hit); 0 for a circle. |
| `ArcDegrees` | 7 | `float` | A cone's full angle in degrees; 0 otherwise. Walls do not clip a cone. |

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

### `SUnitHealedPacket` (`SMSG_UNIT_HEALED = 0x310C`, encrypted, TCP)

Server → the healer, the target, and every client within `Game:InterestRadius` of the target (#506, #532). A heal that restored more than 0 health; a heal on a target at full health sends nothing.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `Healer` | 1 | `ulong` | Raw `ObjectGuid` of the healer. |
| `Target` | 2 | `ulong` | Raw `ObjectGuid` of the unit healed. |
| `Amount` | 3 | `uint` | Health actually restored, overheal left out. |
| `CurrentHealth` | 4 | `uint` | The target's health after the heal. |
| `AbilityId` | 5 | `uint?` | The ability that healed; absent when none did. |
| `Result` | 6 | `HitResult` | `Crit` for a critical heal, otherwise `None`; a heal is never dodged or blocked. |

### `SCharacterStatsPacket` (`SMSG_CHARACTER_STATS = 0x302A`, encrypted, TCP)

Server → the player's own client only. The character sheet (#506), always whole: sent on the tick the character enters the world, and again, at most once per tick, whenever a value changes (a gear change, a level-up, or a `/reload combat` that moves a cap). Replace what is shown with each packet.

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `Stamina` | 1 | `uint` | |
| `Strength` | 2 | `uint` | |
| `Agility` | 3 | `uint` | |
| `Intellect` | 4 | `uint` | |
| `Armor` | 5 | `uint` | |
| `AttackDamage` | 6 | `uint` | |
| `AbilityDamage` | 7 | `uint` | |
| `CritPct` | 8 | `float` | Percentage points, already clamped to the formula's `CritCap`. |
| `DodgePct` | 9 | `float` | Percentage points, already clamped to `DodgeCap`. |
| `BlockPct` | 10 | `float` | Percentage points, already clamped to `BlockCap`. |
| `WeaponMin` | 11 | `uint` | The main-hand weapon's damage range; both 0 with no weapon. |
| `WeaponMax` | 12 | `uint` | |

### Ability amounts for tooltips (#669)

What each learned ability deals or heals per unit hit, worked out by the server from the character's current stats, so a tooltip can say `Damage: 44–48` without copying the combat formula.

**At select**, every `AbilityInfo` in `SCharacterAbilitiesPacket` (`SMSG_CHARACTER_ABILITIES`) carries:

| Field | Proto # | Type | Notes |
|---|---|---|---|
| `AmountKind` | 18 | `AbilityAmountKind` | `None` 0, `Damage` 1, `Healing` 2. `None` means the ability has no direct amount to state: show no line (it is not an amount of 0). |
| `AmountMin` | 19 | `uint` | At the low end of the weapon roll. |
| `AmountMax` | 20 | `uint` | At the high end. Equal to `AmountMin` for an ability with no weapon term. |

**Afterwards**, `SCharacterAbilityAmountsPacket` (`SMSG_CHARACTER_ABILITY_AMOUNTS = 0x302C`, encrypted, TCP, the player's own client only) carries `Amounts` (field 1), one `AbilityAmountInfo` per learned ability: `AbilityId` 1, `Kind` 2, `Min` 3, `Max` 4, with the meanings above. It is sent, whole, whenever a stats refresh (a gear change, a level-up) moves any amount, in the same tick's flush as the `SCharacterStatsPacket` showing the new stats, and never when nothing moved. Replace every ability's amount with it. Its lifetime is the character's session: a character switch (Change Character, #663) or a new login selects again, and the new select's `SMSG_CHARACTER_ABILITIES` starts over.

**What the numbers are.** The ability's base, exactly as combat computes it: `EffectValue + ScalingCoefficient × (AttackDamage or AbilityDamage) + BaseDamageCoefficient × weapon roll`, the roll uniform and inclusive over the main hand's `WeaponMin..WeaponMax` (only when the coefficient and the weapon are above 0). Then floored as a normal hit resolves it: **before crit, dodge, block and the target's armour** (a target with no armour takes exactly this on a normal hit). Damage has a minimum of 1; healing has none, and a heal on a unit at full health restores less. The kind follows `Affects`: `Ally` is healing, anything else damage. Only the three shape scripts (circle, cone, projectile) apply an amount directly; an ability run by any other script is `None`.

## 5. Cast Pipeline Expectations (Client Side)

- Click → emit `CCastAbilityPacket {AbilityId, GroundPos?}` (`GroundPos` for a Cursor skill, and for a Movement skill so it points at the cursor rather than along the facing, #716: a player walking backwards swings toward the cursor, not behind). Draw a Movement skill's preview toward the cursor too; the telegraph and fired broadcast carry the direction the server used. The server validates everything and answers every refusal with a reason.
- On rejection, the server replies with exactly one `SAbilityNotReadyPacket {AbilityId, CooldownMs (uint), Reason (CastRejectReason)}` (#512). `Reason` names the refusal: `Gcd`, `Cooldown`, `RequiresOutOfCombat`, `RequiresInCombat`, `NotEnoughPower`, `Dead`, `NotOwned`, `InternalError`, `NoAimPoint`, `AlreadyCasting` or `WrongPowerType` (14, #652: the ability's cost is spent from a pool the caster does not have, a Mana ability on a Warrior say; `AbilityInfo.CostPowerType` names the pool, so a client can grey such an ability out); `Unknown` (0) is only what a payload without the field decodes as. `OutOfRange`, `TargetNotFound` and `NotFacing` remain in the enum but are no longer sent (#164). `CooldownMs` is the remaining time in milliseconds for `Gcd` and `Cooldown`, rounded up so it is always at least 1, and 0 for every other reason. The only cast that gets no answer is one from a connection with no character.
- A cast while another is in progress is refused as `AlreadyCasting`. A cast-time cast fires with the aim it started with: facing (for a Movement skill, the direction toward `GroundPos` when one was sent) and ground point are captured when it starts.
- `AbilityInfo.FacingAngle` in `SMSG_CHARACTER_ABILITIES` is no longer set (#164): there is no facing cone.
- For abilities with `CastTime > 0`, every client near the caster (or near a circle's centre) receives `SUnitStartCastPacket {Caster, CastTime, AbilityId, CastId, Footprint}`: render the cast bar from `CastTime`, show which ability from `AbilityId` (#521 item 9), and draw the telegraph from `Footprint` (#648) for the whole cast. `CastId` (never 0 from a current server) identifies the cast: the finish, the interrupt and the fired broadcast of the same cast carry it. `Footprint` is fixed for the cast; it is what fires.
- On completion, the server sends `SUnitFinishCastPacket {Caster, AbilityId, CastId}`. End the cast bar and play the cast-finish animation.
- On an interrupt (the caster moved, died, left, or a creature turned for home), the server sends `SCharacterInterruptedCastPacket {Caster, AbilityId, CastId}`. Power spent on the cast is not refunded; the client just ends the cast bar.
- **Item cast bars (item use).** An item's cast bar uses the same three packets with `AbilityId` 0 and `ItemTemplateId` set (start field 6, finish and interrupt field 4), drawn as a plain bar with no telegraph (it has no footprint). Its `CastId` comes from the same per-instance sequence as ability casts, so key and clear it the same way. See `docs/item-use-protocol.md`.
- **Clearing a telegraph (#648):** key it by `Caster` and `CastId`. Clear it on the finish or interrupt with that id, on a new start from the same caster, when the caster leaves the client's view (a world-state remove), or once `CastTime` has passed, whichever comes first; the last two cover a finish the client was too far away to hear. A payload with `CastId` 0 comes from a server before #648: fall back to the caster alone.
- **Instant abilities** (`CastTime = 0`) have no start and no telegraph: they fire on the tick they are cast. A circle or cone sends `SAbilityFiredPacket` with the whole `Footprint`; a projectile is replicated as a world object.

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
- [ ] Heal a wounded ally → the healer, the ally and nearby clients get `SMSG_UNIT_HEALED` with the restored `Amount`; a crit heal has `Result = Crit`; healing someone at full health sends nothing.
- [ ] Log in, then equip gear that changes a stat → `SMSG_CHARACTER_STATS` arrives once at login and once after the equip, to the player alone.
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

Each class starts with three skills (#164), ordinary `AbilityTemplate` rows seeded by the World migration `SeedStarterSkillKit`. They are granted on character creation through each class's `CharacterCreateInfos.StartingSpells`, and the Character migration `GrantStarterSkillKit` gave every existing character its class's three in place of whatever it held. The client retrieves the player's full ability list via `SCharacterAbilitiesPacket` on character login, and learns each skill's shape from `AbilityInfo` fields 8-16 (`AimMode`, `Shape`, `Anchor`, `Reach`, `Radius`, `ArcDegrees`, `ProjectileSpeed`, `Pierce`, `Affects`), which is everything it needs to draw a telegraph. Field 17, `CostPowerType` (#652), names the pool `Cost` is spent from (None for a free ability).

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

# Aura Protocol

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** auras, V1. Amended in place when the aura protocol changes.

This is the client contract for auras. The wire schema is `schema/avalon.proto` (re-exported with
`tools/Avalon.Exporter`); field numbers below are the `[ProtoMember]` numbers in it. Server-side rules are
described in `CLAUDE.md` under "Auras".

## 1. What an aura is

An aura is a timed effect on a character or a creature: periodic damage (a damage-over-time aura), periodic
healing (a heal-over-time aura), stat modifiers (a buff, a debuff, a slow), or both. Every aura is **helpful** or
**harmful**. Abilities put them on the units their shape affects (a Hostile ability only harmful auras, on hostile
units; an Ally ability only helpful ones, on allies), and item scripts can put helpful auras on their user and its
party members in the same instance.

The client needs no aura data of its own beyond the catalog: names, icons, kinds, timing, stacking and stat
modifiers come from `schema/auras/aura-catalog-v1.json`, keyed by aura id, with every enumeration written as a
number and listed in its `$comment` (`kind` 1 helpful, 2 harmful; `periodicKind` 0 none, 1 damage, 2 heal;
`stacking` 1 refresh, 2 stack up to `maxStacks`, 3 one per caster; modifier `stat` 1 armor, 2 attack damage,
3 ability damage, 4 crit %, 5 dodge %, 6 block %, 7 haste %, 8 movement speed %, 9 max health, 10 max power;
modifier `kind` 1 flat, 2 percent). In `schema/abilities/ability-catalog-v1.json`, an ability's `auraId` names the
aura it applies, or is `null`.

## 2. Opcodes

Client to server. Accepted only while the character is in the world (in a map).

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `CMSG_AURA_CANCEL` | `0x2104` | `CAuraCancelPacket` | 1 `AuraId` (uint32), 2 `InstanceKey` (optional uint32) |

Server to client.

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `SMSG_AURA_UPDATE` | `0x310D` | `SAuraUpdatePacket` | 1 `UnitGuid` (uint64, raw `ObjectGuid`), 2 `Entries` (repeated `AuraEntryDto`) |
| `SMSG_AURA_LIST` | `0x310E` | `SAuraListPacket` | 1 `UnitGuid` (uint64, raw `ObjectGuid`), 2 `Entries` (repeated `AuraEntryDto`) |
| `SMSG_AURA_CANCEL_RESULT` | `0x310F` | `SAuraCancelResultPacket` | 1 `AuraId` (uint32, echoed), 2 `Result` (`AuraCancelResult`) |

`AuraEntryDto`, one aura on one unit:

| Field | # | Type | Meaning |
|---|---|---|---|
| `AuraId` | 1 | uint32 | The aura, an id of the aura catalog. |
| `InstanceKey` | 2 | uint32 | This copy's handle, unique among the auras the unit holds, never 0, and the same for as long as the unit holds the copy. A unit holds one copy of an aura, except a "one per caster" aura (`stacking` 3), of which it holds one copy per caster. |
| `CasterGuid` | 3 | uint64 | Raw `ObjectGuid` of whoever applied it last; 0 for nobody, or nobody known. |
| `Stacks` | 4 | uint32 | Its stacks, 1 or more. |
| `RemainingMs` | 5 | uint32 | Milliseconds left (section 7); 0 on a removal. |
| `DurationMs` | 6 | uint32 | Its whole duration when last applied, for drawing a timer. |
| `Action` | 7 | `AuraUpdateAction` | What happened (updates only; absent in a list). |

Enumerations (append-only; `Unknown = 0` is what a payload without the field decodes as):

- `AuraUpdateAction`: `Unknown` 0, `Applied` 1 (a new copy), `Refreshed` 2 (applied again: its duration and its
  amounts renewed), `Stacked` 3 (one more stack, renewed too), `Removed` 4 (it ended; `RemainingMs` 0).
- `AuraCancelResult`: `Unknown` 0 (never sent), `Ok` 1, `NotFound` 2, `NotCancellable` 3, `Dead` 4.

## 3. Lists

`SMSG_AURA_LIST` carries every aura a unit holds. **Replace whatever the client holds for that unit with it.** It is
sent, in the tick's state broadcast and after that tick's `SMSG_WORLD_STATE_ADD`, so the client always knows the unit
before it hears its auras:

- for every unit that comes into the client's view **and holds at least one aura**: a unit that comes into view
  holding none gets no list, so a unit added without one holds none;
- for the client's **own character every time it is added**, empty too: on entering the world (login, a Change
  Character), on every map change and on a respawn, since every move between instances starts replication over
  (see `SMSG_WORLD_STATE_REMOVE` below). Auras survive the move; the list is how the client learns them again.

The entries of a list have no `Action`. A unit's list replaces that tick's updates for it: a unit listed in a tick
gets no `SMSG_AURA_UPDATE` in the same tick.

**Leaving view.** When a unit leaves the client's view it is named in an ordinary `SMSG_WORLD_STATE_REMOVE`. Drop the
unit's auras with it. Nothing is sent about its auras while it is out of view; if it comes back, it is a fresh add,
followed by a fresh list when it holds any.

## 4. Updates

`SMSG_AURA_UPDATE` carries one unit's aura changes of one tick, **in the order they happened**, at most one packet per
unit per tick. It goes to the unit itself (when it is a character) and to every client that has the unit in view
(interest, `Game:InterestRadius`, 60 m by default, as for the unit's own world state), in the same tick's broadcast.
Each entry is the copy as it stood when that change happened, so one packet can hold several entries for one key (a
copy applied and removed in the same tick, say): apply them in order.

- `Applied`: add the copy under its `InstanceKey`.
- `Refreshed` / `Stacked`: replace the copy's stacks, caster, remaining time and duration.
- `Removed`: drop the copy.

An update for a key the client does not hold is ignored. A client that has the unit in view has been sent its
list, so an unknown key should not occur.

## 5. Ticks

A tick of a damage aura arrives on the existing damage packets, a tick of a heal aura on the existing heal packet,
with **`AuraId` set** and no `AbilityId`:

| Packet | `AuraId` field | To |
|---|---|---|
| `SCharacterDamagePacket` (`SMSG_CHARACTER_DAMAGED`) | 7 | the character hurt, first |
| `SUnitDamagePacket` (`SMSG_CREATURE_DAMAGED`) | 6 | everyone who hears the hit |
| `SUnitHealedPacket` (`SMSG_UNIT_HEALED`) | 7 | everyone who hears the heal |

A tick is heard as a hit or a heal is: by the caster, the target and every client within `Game:InterestRadius` of the
target. The attacker (or healer) is the aura's caster while it is alive in the target's instance, and raw 0 otherwise
(nobody applied it, or its caster died, left the instance or logged out: the aura keeps ticking on what it took from
the caster when it was applied).

Ticks are never dodged or blocked; a tick can crit (`Result` `Crit`), and a damage tick is reduced by the target's
armour. **A tick that comes to nothing sends nothing:** a damage tick worth less than a whole point (the fraction is
carried to the next tick, so the ticks add up to the aura's total), and a heal tick that restored no health (a target
at full health). A killing tick is sent like any killing hit, before `SMSG_UNIT_DEATH`.

## 6. Rules a client shows

- **Ticks** come every tick interval of the aura, the last exactly at its expiry: an aura of duration D and interval I
  ticks `max(1, floor(D / I))` times, at `expiry - j x I` (j counting down to 0). An aura with interval 0 never ticks.
- **The amount** is fixed when the aura is applied, from the caster's stats then: the aura's base, plus its scaling
  coefficient times the caster's attack or ability damage, plus its base damage coefficient times one roll of the
  caster's base damage (a character's main hand, a creature's natural range), split evenly over the ticks. Each tick
  rolls crit with the caster's crit chance as it was at apply time.
- **Applied again**, by its stacking: a refresh aura renews its duration and amounts (a fresh snapshot and a fresh
  roll of the base damage); a stack aura gains a stack up to its `maxStacks` and renews the same way; a "one per
  caster" aura keeps a separate copy for each caster, each renewed by its own caster only. Stacks multiply the
  amounts and the stat modifiers.
- **A unit holds at most `Game:MaxAurasPerUnit` auras** (32 by default); past that a new aura is not applied. A copy
  the unit already holds still refreshes or stacks.
- **A dodged hit applies no aura.** An ability that only applies an aura (no direct damage or heal) is never dodged.
  A target that takes no hits (an invulnerable town creature, a corpse, a creature walking home) gets no harmful aura.
- **A harmful aura starts combat**, as a hit does: the creature engages its caster, and both are in combat.
- **Auras survive map changes and logout.** Their time does not run while the character is offline, nor while it
  loads into the world after a login; it starts again when the character enters its instance. Across a map change it
  runs on.
- **Death ends every aura**, sent as `Removed` entries.
- **A creature that gives up a fight and walks home** loses the harmful auras the fight put on it.
- **Stats already include auras.** `SMSG_CHARACTER_STATS`, the movement speed the server steps at and the ability
  amounts (`SMSG_CHARACTER_ABILITY_AMOUNTS`) are sent with every aura folded in, whenever an aura with stat modifiers
  is applied, stacked or ends; a slowed creature's world-state velocity is its slowed speed. Do not apply a catalog
  modifier on top.

## 7. Remaining time

`RemainingMs` is rounded up, so an aura with any time left never reads 0, and counts from the moment the entry was
made: in a list, when the list was sent; in an update, when the change happened, which is the tick the packet leaves
in. Count it down locally from receipt. `DurationMs` is the whole duration of the copy's last application, so
`DurationMs - RemainingMs` is how far through it is. An aura restored at login comes with the time it had left when
the character last left the world.

## 8. Cancelling an aura

A player can end its **own helpful** auras. `CMSG_AURA_CANCEL` names the aura by `AuraId` and, optionally, one copy by
its `InstanceKey`:

- with `InstanceKey`, only the copy with that key ends;
- without it (a client that does not send one), every copy of that aura on the sender's character ends.

Every cancel from a character in the world gets **exactly one** `SMSG_AURA_CANCEL_RESULT`, echoing the `AuraId`. The
checks run in this order:

1. `Dead`: the sender's character is dead; nothing ends.
2. `NotFound`: it holds no copy of the aura (with that key, when one was given). A request the server failed to handle
   is answered `NotFound` too.
3. `NotCancellable`: the aura is harmful. Harmful auras cannot be cancelled.
4. `Ok`: the copy (or every copy) ended.

The result is sent at once, so it arrives **before** that tick's `SMSG_AURA_UPDATE`, whose `Removed` entries are what
actually take the copies off the unit for the sender and for everyone watching it. A connection with no character gets
no answer.

## 9. Seeded auras

| Id | Name (icon) | Kind | Duration / tick | Periodic | Stacking | Modifiers |
|---|---|---|---|---|---|---|
| 1 | Bleed (`bleed`) | Harmful | 12 000 / 3 000 ms (4 ticks) | Damage: 12 + Attack x 0.25 | Stack, up to 3 | |
| 2 | Burn (`burn`) | Harmful | 9 000 / 3 000 ms (3 ticks) | Damage: 24 + Ability x 0.6 | Refresh | |
| 3 | Crippled (`crippled`) | Harmful | 6 000 ms, no ticks | | Refresh | Movement speed flat -30 (a 30 % slow) |
| 4 | Renew (`renew`) | Helpful | 12 000 / 3 000 ms (4 ticks) | Heal: 24 + Ability x 0.4 | Refresh | |
| 5 | Fortified (`fortified`) | Helpful | 30 000 ms, no ticks | | Refresh | Armor percent +20 |
| 6 | Poison (`poison`) | Harmful | 9 000 / 3 000 ms (3 ticks) | Damage: 3 + 1.0 x one roll of the caster's base damage | Stack, up to 3 | |
| 7 | Sundered (`sundered`) | Harmful | 10 000 ms, no ticks | | Refresh | Armor percent -25 |

The periodic amount is the total per stack, split over the ticks. At level 1 (a Warrior's attack 46, a Wizard's
ability damage 69, a Healer's 46) that is 23.5 for Bleed, 65.4 for Burn and 42.4 for Renew. Armour auras are
percentages: they scale the armour a unit already has and do nothing to a unit with none.

| Ability | Who | Shape | Aim | Reach, size | Cooldown | Cost | Direct | Aura |
|---|---|---|---|---|---|---|---|---|
| 203 Rend | Warrior | Cone | Movement | reach 2.5, arc 90 | 6 s | 10 Fury | Damage: 8 + Attack x 0.2 + weapon x 0.5 | 1 Bleed |
| 213 Ignite | Wizard | Circle on the aim point | Cursor | reach 18, radius 3 | 6 s | 20 Mana | none | 2 Burn |
| 223 Crippling Shot | Hunter | Projectile, 28 m/s | Cursor | reach 25 | 8 s | 15 Energy | Damage: 14 + Attack x 0.35 + weapon x 0.6 | 3 Crippled |
| 233 Renew | Healer | Ally circle on the aim point | Cursor | reach 15, radius 4 | 6 s | 15 Mana | none | 4 Renew |
| 234 Fortify | Healer | Ally circle on the caster | Movement | radius 8 | 20 s | 20 Mana | none | 5 Fortified |
| 317 Venom Spit | Blightfly Swarmling | Projectile, 14 m/s | Cursor | reach 10 | 8 s | none | Damage: natural roll x 0.6 | 6 Poison |
| 318 Sundering Howl | Bramblemaw Alpha | Circle on the caster | Movement | radius 6 | 18 s | none | none | 7 Sundered |

All are instant. Every character of a class holds its class's new abilities. An Ally circle affects the caster too when
the circle covers it.

# Aura Protocol

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** auras, V1. Amended in place when the aura protocol changes.

This is the client contract for auras. The wire schema is `schema/avalon.proto` (re-exported with
`tools/Avalon.Exporter`); field numbers below are the `[ProtoMember]` numbers in it. Server-side rules are
described in [Auras (server design)](auras.md).

## 1. What an aura is

An aura is a timed effect on a character or a creature: periodic damage (a damage-over-time aura), periodic
healing (a heal-over-time aura), stat modifiers (a buff, a debuff, a slow), or both. Every aura is **helpful** or
**harmful**. Abilities put them on the units their shape affects (a Hostile ability only harmful auras, on hostile
units; an Ally ability only helpful ones, on allies), and item scripts can put helpful auras on their user and its
party members in the same instance.

The client needs no aura data of its own beyond the catalog: names, icons, kinds, timing, stacking, crowd control,
break chances and stat modifiers come from `schema/auras/aura-catalog-v1.json`, keyed by aura id, with every
enumeration written as a number and listed in its `$comment` (`kind` 1 helpful, 2 harmful; `periodicKind` 0 none,
1 damage, 2 heal; `stacking` 1 refresh, 2 stack up to `maxStacks`, 3 one per caster; `control` flags 1 stun, 2 root,
4 silence, 0 none; `breakOnDamagePct` and `breakPerSecondPct` in percent; modifier `stat` 1 armor, 4 crit %, 5 evasion, 6 block %,
8 movement speed %, 9 max health, 10 max power, 11 fire resistance, 12 cold resistance, 13 lightning resistance,
14 blight resistance, 15 max ward, and since #1178 16-28 the increased percentages (damage, physical, fire, cold,
lightning, blight, attack, spell, area, projectile, melee and damage over time damage, then healing), 29 crit
multiplier (points: 50 makes a 1.5 crit 2.0), 30 attack speed %, 31 cast speed %, 32 cooldown recovery %, with 2, 3
and 7 retired and never reused; modifier `kind` 1 flat, 2 increased, 3 more, an increased stat 16-28 taking flat or
more, never increased). The catalog's `control` is for tooltips only: a unit's crowd control is read from
each aura entry's `Control` (section 10), which also carries the slow and stays right after a live edit. In
`schema/abilities/ability-catalog-v1.json`, an ability's `auraId` names the aura it applies, or is `null`, and
`basic` marks a class's basic attack, which a silence does not stop (section 10).

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
| `Control` | 8 | uint32 | The crowd control this copy puts its holder in, as bits: 1 stun, 2 root, 4 silence, 8 slow; 0 for none (section 10). In lists and updates alike. |

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
(interest, `Game:InterestRadius`, 50 m by default, as for the unit's own world state), in the same tick's broadcast.
Each entry is the copy as it stood when that change happened, so one packet can hold several entries for one key (a
copy applied and removed in the same tick, say): apply them in order.

- `Applied`: add the copy under its `InstanceKey`.
- `Refreshed` / `Stacked`: replace the copy's stacks, caster, remaining time, duration and control.
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

Ticks are never evaded or blocked; a tick can crit (`Result` `Crit`), and a damage tick is reduced by the target's
armour (a physical tick) or its resistance (an element) (#1178). A damage tick carries the aura's damage type in
`Type`, the same in `Types`, and what the target's ward took in `WardAbsorbed`; a blight tick or a physical one (a
bleed) passes the ward. `Damage` is the whole tick, the ward's part included. **A tick that comes to nothing sends nothing:** a damage tick worth less than a whole point (the fraction is
carried to the next tick, so the ticks add up to the aura's total), and a heal tick that restored no health (a target
at full health). A killing tick is sent like any killing hit, before `SMSG_UNIT_DEATH`.

## 6. Rules a client shows

- **Ticks** come every tick interval of the aura, the last exactly at its expiry: an aura of duration D and interval I
  ticks `max(1, floor(D / I))` times, at `expiry - j x I` (j counting down to 0). An aura with interval 0 never ticks.
- **The amount** is fixed when the aura is applied, from the caster's stats then (#1178): the aura's flat base plus a
  percentage of one roll of its source ability's base (the caster's weapon for an attack, a character's main hand or
  bare hand and a creature's natural range; the ability's own spell base for a spell or a heal), scaled by the
  caster's damage modifiers for the source's tags plus damage over time (healing modifiers for a heal), split evenly
  over the ticks. Nothing the caster does afterwards changes it. Each tick rolls crit with the caster's crit chance
  and crit multiplier as they were at apply time. The amounts are the server's: the catalog carries neither the base
  nor the percentage.
- **Applied again**, by its stacking: a refresh aura renews its duration, never shortening what is left, and its
  amounts (a fresh snapshot and a fresh roll of the base damage); a stack aura gains a stack up to its `maxStacks` and
  renews the same way; a "one per caster" aura keeps a separate copy for each caster, each renewed by its own caster
  only. Stacks multiply the amounts and the stat modifiers.
- **A unit holds at most `Game:MaxAurasPerUnit` auras** (32 by default); past that a new aura is not applied. A copy
  the unit already holds still refreshes or stacks.
- **An evaded hit applies no aura.** Every hostile ability rolls whether it lands on each unit it affects, whether it
  deals damage or not (#1164): an evaded one applies no aura and is sent as a hit of 0 marked `Dodged` (which means
  evaded), naming the ability and its type, and a landed one with no damage sends only the aura, no damage number. An
  ability flagged `Unavoidable` is never evaded (none is seeded). Heals, helpful auras and aura ticks are never
  evaded. A target that
  takes no hits (an invulnerable town creature, a corpse, a creature walking home) gets no harmful aura. No ability
  reaches a creature walking home, `Unavoidable` or not: its shape passes over it (#1181). Only crowd control applied
  to it directly, by a Resonance proc, is answered "Immune" (section 10).
- **A harmful aura starts combat**, as a hit does: the creature engages its caster, and both are in combat.
- **Auras survive map changes and logout.** Their time does not run while the character is offline, nor while it
  loads into the world after a login; it starts again when the character enters its instance. Across a map change (a
  portal, a scroll, a respawn, a party's return to town) it pauses while the character is between instances and runs
  on once it arrives, so the move costs no tick and pays none in a burst.
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
| 1 | Bleed (`bleed`) | Harmful | 12 000 / 3 000 ms (4 ticks) | Damage: 12 + 21 % of the source's weapon roll | Stack, up to 3 | |
| 2 | Burn (`burn`) | Harmful | 9 000 / 3 000 ms (3 ticks) | Damage: 100 % of the source's spell base | Refresh | |
| 3 | Crippled (`crippled`) | Harmful | 6 000 ms, no ticks | | Refresh | Movement speed flat -30 (a 30 % slow) |
| 4 | Renew (`renew`) | Helpful | 12 000 / 3 000 ms (4 ticks) | Heal: 100 % of the source's spell base | Refresh | |
| 5 | Fortified (`fortified`) | Helpful | 30 000 ms, no ticks | | Refresh | Armor increased +20 |
| 6 | Poison (`poison`) | Harmful | 9 000 / 3 000 ms (3 ticks) | Damage: 3 + 100 % of one roll of the caster's natural range | Stack, up to 3 | |
| 7 | Sundered (`sundered`) | Harmful | 10 000 ms, no ticks | | Refresh | Armor increased -25 |
| 12 | Stunned (`stunned`) | Harmful | 1 500 ms, no ticks | | Refresh | none; stun |
| 13 | Bound (`bound`) | Harmful | 3 000 ms, no ticks | | Refresh | none; root |
| 14 | Frozen (`frozen`) | Harmful | 2 500 ms, no ticks | | Refresh | none; root |
| 15 | Silenced (`silenced`) | Harmful | 3 000 ms, no ticks | | Refresh | none; silence |

The periodic amount is the total per stack, split over the ticks. At level 1 with the starter gear #1179 retired (a
Warrior's Strength 29 and its 4-7 sword, a Wizard's Intellect 31, a Cleric's 29) that was 25.9-27.1 for Bleed (from
Rend), 64.6-95.9 for Burn (from Ignite) and 38.3-56.4 for Renew; the bases that replaced it give no attributes, so each
amount is somewhat lower in them. Armour auras are increased modifiers: they scale the armour a unit already has and do nothing to a unit with none. Crippled (3) and Hexed (10)
are slows. Break chances (on damage / on its own, per second): Stunned 10 % / 0.5 %, Bound and Frozen 25 % / 0.5 %,
Silenced never, Crippled and Hexed 0 % / 0.5 %.

| Ability | Who | Shape | Aim | Reach, size | Cooldown | Cost | Direct | Aura |
|---|---|---|---|---|---|---|---|---|
| 203 Rend | Warrior | Cone | Movement | reach 2.5, arc 90 | 6 s | 10 Mana | Damage: an attack at 202 % of the weapon | 1 Bleed |
| 213 Ignite | Wizard | Circle on the aim point | Cursor | reach 18, radius 3 | 6 s | 20 Mana | none (a spell: its spell base, 31-46 growing 0.9 % a level, is Burn's) | 2 Burn |
| 223 Crippling Shot | Hunter | Projectile, 14 m/s | Cursor | reach 25 | 8 s | 15 Mana | Damage: an attack at 495 % of the weapon, half converted to cold | 3 Crippled |
| 233 Renew | Cleric | Ally circle on the aim point | Cursor | reach 15, radius 4 | 6 s | 15 Mana | none (a heal: its spell base, 19-28, is Renew's) | 4 Renew |
| 234 Fortify | Cleric | Ally circle on the caster | Movement | radius 8 | 20 s | 20 Mana | none | 5 Fortified |
| 317 Venom Spit | Blightfly Swarmling | Projectile, 14 m/s | Cursor | reach 10 | 8 s | none | Damage: an attack at 60 % of the natural roll | 6 Poison |
| 318 Sundering Howl | Bramblemaw Alpha | Circle on the caster | Movement | radius 6 | 18 s | none | none | 7 Sundered |
| 204 Shield Bash | Warrior | Cone | Movement | reach 2.5, arc 90 | 15 s | 15 Mana | Damage: an attack at 148 % of the weapon | 12 Stunned |
| 214 Frost Bind | Wizard | Circle on the aim point | Cursor | reach 18, radius 3 | 20 s | 25 Mana | Damage: a spell of 6-9, growing 0.45 % a level | 14 Frozen |
| 224 Binding Shot | Hunter | Projectile, 14 m/s | Cursor | reach 25 | 15 s | 20 Mana | Damage: an attack at 168 % of the weapon, plus 2-4 blight | 13 Bound |
| 235 Hush | Cleric | Projectile, 17 m/s | Cursor | reach 18 | 20 s | 20 Mana | none | 15 Silenced |

**Longer and deeper under a capstone (#1181 part D).** A caster holding a capstone that shapes the ability may place
its aura for longer than the row's duration (Wildfire's Burn 13.5 s, Enduring Renewal's Renew 18 s, Steadfast's
Fortified 45 s), with the extra ticks that time holds, each the same size; and may stack it above the row's
`MaxStacks` (Haemorrhage: Bleed to 5). Draw the timer from the entry's `DurationMs` and the count from its `Stacks`,
never from this table. A stack count above the row's cap comes only from the shaped caster's applications; another
caster's refresh keeps the count the copy holds, and never shortens the time it has left, so its `DurationMs` may then
be that time rather than the row's. A count above the cap comes back as it was after a relog.

All are instant. Every cost is mana, for every class (#1181). Every character of a class holds its class's new
abilities. An Ally circle affects the caster too when the circle covers it.

## 10. Crowd control

A harmful aura can put its holder under crowd control. Read a unit's state from the `Control` of every aura entry it
holds (the union of their bits), never from the catalog:

| Bit | State | The unit cannot |
|---|---|---|
| 1 | Stun | move, turn, cast (its basic attack included), use an item or a doodad, put gear on or off, touch the Bank, pick up loot, talk to an NPC or act on an open conversation, enter a portal. It can still cancel its own helpful auras, move items within its Bag and chat. |
| 2 | Root | move. It still turns, casts and acts within reach. |
| 4 | Silence | cast any ability but its class's basic attack, the abilities whose `basic` is true in `schema/abilities/ability-catalog-v1.json`. It still moves and uses items (an item that casts included) and doodads. |
| 8 | Slow | move at full speed. The slowed speed already reaches the client (section 6); the bit is for display. |

- **Movement.** While the own character holds a stun or a root, stop predicting movement locally. The server takes
  the input but makes no step: every `SPlayerStateAckPacket` carries the unchanged position and zero velocity, with
  the input's yaw while rooted and the character's own yaw while stunned.
- **Refusals** carry their reason: `SAbilityNotReadyPacket` `CastRejectReason` `Stunned` 15 or `Silenced` 16;
  `ItemUseResult` `Stunned` 17; `ItemRequestResult` `Stunned` 13 (a drag or destroy touching Equipment or the Bank);
  `VendorResult` `Stunned` 13; `QuestResult` `Stunned` 11; `LootPickupResult` `Stunned` 7; `MapTransitionResult`
  `Stunned` 14. An open NPC conversation stays open. An interaction with an NPC or a doodad, and a dialogue choice,
  are dropped with no answer, as their other refusals are.
- **Interrupts.** A stun ends the unit's running cast bar, an ability's, an item's or a doodad's; a silence ends an
  ability cast unless it is the basic attack or an item's. Each ends with the usual interrupt packet and its cast id;
  a hit the cast already fired still lands.
- **Immune.** Crowd control a target resists whole is not applied, and the caster and everyone who would have seen
  the hit get a damage packet of 0 with `Result` `HitResult.Immune` (8), after the ability's own damage packet if it
  has one: on a creature whose resistance to that type is 0 (a boss is immune to stun and silence), on a character
  that has already taken a stun, root or silence of that type three times from other characters within the
  diminishing window, and on a creature walking home given it directly, by a Resonance proc. No ability
  reaches a creature walking home, `Unavoidable` or not: its shape passes over it, so it takes no damage, no aura and
  no "Immune", and a capped ability hits the nearest unit it can damage instead (#1181).
- **Shorter durations.** Resistance and diminishing shorten an aura rather than refuse it: a Rare creature takes half
  of each, a Boss half a root or slow, and a character takes a stun, root or silence from another character at full,
  then half, then a quarter of its duration until none of that type has landed for 6 s
  (`Game:CrowdControlDiminishWindow`). The entry's `DurationMs` is the shortened duration, so draw the timer from the
  entry. A refresh never shortens what is left: its `DurationMs` may then be the time left rather than the row's
  duration.
- **Never lengthened.** No capstone lengthens or deepens crowd control: a crowd-control aura's `DurationMs` is
  never above its row's, and its stacks never above its `MaxStacks`.
- **Breaking early.** A crowd-control aura can end before its time, by chance, when its holder takes damage or once
  per second held (the catalog's break chances). It arrives as an ordinary `Removed` entry.

## 11. Resonance effect auras (#1180)

Worn gear applies auras too: a theme's Resonance effects ([item modifiers](item-modifiers.md#resonance-themes)) fire on
the wearer's hits, critical hits, kills, hits taken, evaded hits and broken wards, and each applies an aura, ids 16-44
(Burning, Chilled, Rimebound, Jolt, Regrowth, Rootsurge, Guard, Barbs, Bleeding, Tailwind, Slipstream, Veiled, Lucid;
[auras](auras.md#resonance-effects-1180) has every row). **No packet, field or enum is new.**

- **The auras** are in `schema/auras/aura-catalog-v1.json` like every other, and the effects that apply them in
  `schema/items/item-modifier-catalog-v1.json` (`themes[].effects`: count, trigger, chance, cooldown, `auraId`, target,
  chain, life threshold, damage type). Every tier of a family shares one icon (`burning`, `chilled`, ...).
- **Applications, renewals and expiry** arrive as `SMSG_AURA_UPDATE` and `SMSG_AURA_LIST` entries (sections 3 and 4),
  with the wearer as `CasterGuid`: on the unit it hit or that struck it for a harmful one, on the wearer for a helpful
  one. A chain (Storm's jolt) is one entry on each creature it reaches, from the same caster in the same tick.
- **Ticks** arrive on the damage and heal packets with `AuraId` and no `AbilityId` (section 5). They never crit. Jolt
  and Barbs last 200 ms with one tick at their end: a client sees each as one hit 0.2 s after its `Applied` entry,
  then a `Removed` entry.
- **A resisted Rimebound** (a creature immune to roots, or a character on its fourth root inside the diminishing
  window, section 10) is the Immune hit of 0 (`HitResult.Immune`) with no `AbilityId`, from the wearer, sent to whoever
  would have seen the hit.

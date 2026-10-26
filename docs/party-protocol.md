# Party Protocol

> **Audience:** client developers and LLMs handed the client codebase.
> **Status:** party play, V1 (2026-09-30). Amended in place when the party protocol changes.

This is the client contract for parties: a party is two or more players who share a procedural (Normal) map
instance, its kills, its loot and its experience. The wire schema is `schema/avalon.proto` (re-exported with
`tools/Avalon.Exporter`); field numbers below are the `[ProtoMember]` numbers in it. Server-side rules are
described in [Parties (server design)](parties.md).

## 1. Opcodes

Client to server. Every one is accepted only while the character is in the world (in a map), and every one
is answered with exactly one `SMSG_PARTY_RESULT` (section 2).

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `CMSG_PARTY_INVITE` | `0x20B0` | `CPartyInvitePacket` | 1 `TargetName` (string, the character's name as the world shows it: 3 to 12 letters A-Z, in any case, and nothing else; anything else is answered `NotFound`, #936) |
| `CMSG_PARTY_INVITE_RESPONSE` | `0x20B1` | `CPartyInviteResponsePacket` | 1 `Accept` (bool) |
| `CMSG_PARTY_LEAVE` | `0x20B2` | `CPartyLeavePacket` | none |
| `CMSG_PARTY_KICK` | `0x20B3` | `CPartyKickPacket` | 1 `CharacterId` (uint32, a member's id from the roster) |
| `CMSG_PARTY_PROMOTE` | `0x20B4` | `CPartyPromotePacket` | 1 `CharacterId` (uint32, a member's id from the roster) |
| `CMSG_PARTY_EXPERIENCE_MODE` | `0x20B5` | `CPartyExperienceModePacket` | 1 `Mode` (`PartyExperienceMode`: `Even` 1, `LevelWeighted` 2) |

Server to client.

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `SMSG_PARTY_INVITE` | `0x30B0` | `SPartyInvitePacket` | 1 `InviterName`, 2 `InviterClass`, 3 `InviterLevel`, 4 `ExpiresInMs` (the invite's lifetime, 60 000 by default) |
| `SMSG_PARTY_RESULT` | `0x30B1` | `SPartyResultPacket` | 1 `Result` (`PartyResult`), 2 `Name` (optional: the other character involved) |
| `SMSG_PARTY_ROSTER` | `0x30B2` | `SPartyRosterPacket` | 1 `PartyId`, 2 `ExperienceMode`, 3 `ModeLockedForMs`, 4 `Members` (repeated `PartyMemberDto`) |
| `SMSG_PARTY_MEMBER_STATUS` | `0x30B3` | `SPartyMemberStatusPacket` | 1 `CharacterId`, 2 `Health`, 3 `MaxHealth`, 4 `Power`, 5 `MaxPower`, 6 `PowerType`, 7 `IsDead` |

`PartyMemberDto`: 1 `CharacterId`, 2 `Name`, 3 `Class`, 4 `Level`, 5 `IsLeader`, 6 `Online`, 7 `SameInstance`.

**Retired.** The old group packets are gone: `CMSG_GROUP_INVITE_RESULT` (`0x200A`), `SMSG_GROUP_INVITE`
(`0x300D`) and `SMSG_GROUP_INVITE_RESULT` (`0x300E`). Their values were deleted from `NetworkPacketType`
(#697) and dropped out of `schema/opcodes.json` and `schema/avalon.proto`, so their numbers are free: a
retired opcode is deleted, and its number may be reused once clients have re-vendored the schema. A client
must delete its handlers and emitters for them.

## 2. Answers

Every request is answered with exactly one `SMSG_PARTY_RESULT`. It arrives after any roster or system line
the request caused, so a client can treat it as the end of the request. `PartyResult` is append-only; 0 is
what a payload without the field decodes as.

| Value | Name | Meaning |
|---|---|---|
| 0 | `Unknown` | Never sent; a payload without the field. |
| 1 | `Ok` | Done (a decline, and a switch to the mode already set (outside the cooldown and combat), are `Ok` too). |
| 2 | `NotLeader` | Only the leader may do that (invite into an existing party, kick, promote, switch the mode). |
| 3 | `NotFound` | The named character is not online, the member is not in your party (or, for a promote, is offline), or the invite's party no longer exists. |
| 4 | `AlreadyInParty` | The target (or, on accepting, you) is already in a party. |
| 5 | `PartyFull` | The party has `MaxPartySize` members (6 by default). |
| 6 | `InvitePending` | The target already holds an invite. |
| 7 | `NoInvite` | You have no invite to answer. |
| 8 | `InCombat` | The mode cannot be switched while any member is in combat. |
| 9 | `OnCooldown` | The mode was switched less than 60 s ago (`ModeLockedForMs` says how long is left). |
| 10 | `Self` | You cannot invite, kick or promote yourself. |
| 11 | `NotInParty` | You are not in a party. |
| 12 | `InviteExpired` | An invite ended unanswered (unasked, below). |
| 13 | `InviteDeclined` | Your invite was declined (unasked, below). |
| 14 | `Invalid` | The request could not be read: an experience mode of `Unknown`. |
| 15 | `Error` | The server failed while handling the request; nothing is known to have changed. |
| 16 | `TooFast` | Too many invites too quickly (#944): the inviter's budget, `Game:PartyInvitesPerMinute` (30) in any 60 s, is spent. Nothing was looked up or sent; try again later. A client built before it reads an unknown value. |

`Name` carries the character involved where the server knows it: the name as sent on an invite, the member's
name on a kick or promote, the other side on an unasked result. A `CMSG_PARTY_INVITE` whose `TargetName` is
not a character name is answered `NotFound` with no `Name` (#936). The `/invite` command is different there:
it puts what was typed in `Name` and in its "Could not find X." line, to the sender only. **Invite budget
(#944):** both share one budget per inviter, every invite that looks its target up spending one; past it
the answer is `TooFast` with the name as sent, and nothing is looked up. The budget limits invite spam;
answers within it are unchanged.

**Unasked results.** Some results arrive without a request:
- `InviteDeclined` (with the target's name) to the inviter when the target declines.
- `InviteExpired` to the inviter (with the target's name) and to the target (with the inviter's name) when an
  invite runs out unanswered, or when either of them goes offline.

**An invite to a player who ignores you** (#723, section 5) is answered `Ok` like any other, but the target is never
sent `SMSG_PARTY_INVITE` and cannot accept it. It stays pending for the inviter (a second invite to the same player is
`InvitePending`) and ends with `InviteExpired` to the inviter only, when it runs out or the target goes offline. One
from an inviter who goes offline ends silently. The refusals before it (`AlreadyInParty`, `InvitePending` for an
invite the target already holds, `PartyFull`, ...) are checked first, exactly as for anyone. An invite already
pending when its target ignores the inviter ends the same way: the target can no longer accept it (`NoInvite`), and
the inviter is told nothing new and sees the usual expiry.

## 3. Roster

`SMSG_PARTY_ROSTER` is the whole party, every time; a client replaces what it held. It goes to every online
member when:
- a member joins, leaves or is kicked;
- the leader changes (a promote, or a handover when the leader leaves or goes offline);
- the experience mode is switched;
- a member comes online (enters the world) or goes offline;
- any member changes instance (map entry through a portal, respawn in town, the leave countdown's move);
- a member levels up.

A member's own client also gets it when it enters the world while in a party.

One change sends it once: when the leader leaves or goes offline, the handover's roster already shows the party
without it (or with it offline), and is the only one sent (#973; before, a second, identical roster followed the
handover's "X is now the party leader." line).

- **No party:** an empty roster, `PartyId` 0 and no members. A character that leaves, is kicked, or whose
  party disbands gets one.
- `Members` are in join order; the first is the longest-standing. Exactly one has `IsLeader`.
- `Online` is false for a member who logged out; it stays a member, with its last known level. An offline
  member cannot be promoted (`NotFound`).
- A leader who leaves while no other member is online hands leadership to the longest-standing member, offline
  as it is; the next time the server finds that leader offline, leadership passes to the first member online.
- A character deleted while in a party stays on its party's roster, shown offline, until the party disbands or
  the server restarts. Kick it to remove it.
- `SameInstance` is relative to the recipient: whether that member is in the recipient's instance. It is
  always true for the recipient's own entry.
- `ModeLockedForMs`: how long until the leader may switch the experience mode again, measured when the
  roster was sent; 0 when it may switch now. A client counts it down locally.
- `Class` is the `CharacterClass` value; `Level` the member's level.

## 4. Member status

`SMSG_PARTY_MEMBER_STATUS` carries one member's pools: health and its maximum, power and its maximum, the
power type, and whether it is dead. Power is mana and the power type `Mana` for every character (#1181);
`Fury` and `Energy` stay in the enum (append-only) but are never sent.
- Only about members in **your own instance** (`SameInstance`), never about yourself (your own values come
  on your character's state stream).
- Sent when a value changes, at most once every 250 ms per member; the latest values follow as soon as the
  interval allows.
- Sent afresh (even unchanged) after a join, an instance change, or a member entering the world, so a
  client that just gained a same-instance member gets its values without waiting for a change.
- A member who is in another instance or offline gets no status; show the roster's entry without pools.

## 5. Chat

**What a client may send (#936).** `CChatMessagePacket.Message` (field 1) is at most **511 bytes of UTF-8**, counted
over the whole field as sent: a command's word, its name and its text together. A chat box of 255 bytes stays inside
it, the longest message being a whisper sent as `/w `, a name of at most 252 bytes, a space and 255 bytes of text. A
longer message is refused whole, never cut: it is said to nobody, no command runs, it does not count against the
rate limit, and the sender gets one `System` line, "Your message is too long and was not sent.". Send valid UTF-8:
each malformed sequence is read as one or more replacement characters, three bytes each, so such a message can
count for more than it took on the wire.

`SChatMessagePacket` has a `Channel` (field 6, `ChatChannel`):

| Value | Channel | What |
|---|---|---|
| 0 | `Say` | A plain message: sent to everyone in the **sender's instance**, the sender included. There is no world-wide chat. |
| 1 | `Party` | `/p <message>`: to every online member of the sender's party, wherever they are, the sender included. |
| 2 | `System` | The server speaking: sender name "System", account and character 0. Command answers and refusals, "Unknown command.", party joins and leaves, leader changes, mode switches, the leave countdown and health scaling. |
| 3 | `Whisper` | `/w <player> <message>` (`/whisper`, #717): to one online player, wherever they are on the world server. The recipient's line has the sender in `CharacterName` and no `TargetName`: show "Aren whispers: hi". The sender gets an echo with its own name in `CharacterName` and the recipient in `TargetName` (field 7, set on the echo only, spelled as the recipient's name is): show "To Kaela: hi". So a whisper line with `TargetName` set is always one you sent. A client from before `Whisper` existed reads 3 as an unknown channel; before `TargetName` existed it reads the echo as a line from yourself. |

Every line also carries `CharacterClass` (field 8, the `CharacterClass` enum the party roster uses, #763, #922): the sending
character's class on `Say`, `Party` and `Whisper` lines, the whisper's echo included (it is the class of the name in
`CharacterName`, which is the sender on both copies; the recipient named in `TargetName` has no class sent). It is `None` (0)
on `System` lines and on a script's whisper (an item's voice), which no character sent; treat `None` as unknown. A client
from before it ignores the field, and a payload from before it reads as `None`.

`/w` refusals are system lines to the sender only, and nothing is sent to anyone else: "Usage: /w <player> <message>"
(no name or no message), "You can't whisper yourself.", and "No player named X is online." (X as typed; an offline and
an unknown name get the same line, so it never reveals who exists, and so does anything that is not a character
name). Names are matched ignoring case; the message is kept as typed, trimmed at the ends.

### Ignore list (#723)

A player can ignore other characters of the same world, online or offline. Everything an ignored character says is
hidden from the player ignoring it: its whispers are not delivered, and its `Say` and `Party` lines are left out for
that player only (everyone else hears them). Its party invites are dropped (section 2). The ignored character is never
told: its whisper gets the usual echo, its invite looks pending until it expires. System lines are not filtered, and
whispering a character you ignore is allowed.

| Opcode | Value | Packet | Fields |
|---|---|---|---|
| `SMSG_IGNORE_LIST` | `0x30D0` | `SIgnoreListPacket` | 1 `Characters` (repeated `IgnoredCharacterDto`, oldest entry first) |

`IgnoredCharacterDto`: 1 `CharacterId` (uint32), 2 `Name` (the name when the list was loaded or the entry added). The
packet is the whole list: it arrives once while a character is selected (before it enters the world, empty too, so a
client replaces whatever it held for an earlier character), after every `/ignore` and `/unignore` that changed the
list, and when a character on the list is deleted while you are online or selected. Replace the list you hold with it.

| Command | Does |
|---|---|
| `/ignore <name>` | Ignore a character of this world, online or offline (name matched ignoring case). Answered "You are now ignoring X.". Refused, with nothing changed: "Usage: /ignore <name>", "You can't ignore yourself.", "X is already on your ignore list.", "Your ignore list is full (50/50)." (the cap is the server's `MaxIgnoredCharacters`), "No character named X exists." (also for anything that is not a character name, which is never looked up). An offline name is looked up in the database, so its answer can arrive a tick or two later. Each `/ignore` that gets past those refusals spends one message of the chat rate limit, as a chat line does, and over the limit it is refused with the same "You're sending messages too fast." line. |
| `/unignore <name>` | Take a character off the list, by the name the list shows. "You are no longer ignoring X." or "X is not on your ignore list.". |
| `/ignorelist` | One line: "Ignoring 2/50: Borin, Kaela." or "You are not ignoring anyone.". |

The list is saved with the character. A deleted character drops off every list.

Commands (typed into chat; they send the same requests as the packets and get the same `SMSG_PARTY_RESULT`,
plus a system line when refused):

| Command | Does |
|---|---|
| `/invite <name>` (`/inv`) | Invite a player. Also answered "You invited X to the party." A name that is not a character name is `NotFound`, "Could not find X." |
| `/leave` | Leave the party. |
| `/kick <name>` | Remove a member (leader only). |
| `/promote <name>` | Make a member the leader (leader only). |
| `/partyxp even` or `/partyxp level` | Switch the experience mode (leader only). Anything else is answered with its usage. |
| `/p <message>` (`/party`) | Party chat. With no text: "Usage: /p <message>". Outside a party: "You are not in a party." |

System lines the party produces (X is a character name):
- "X joined the party." / "X left the party." / "X was removed from the party."
- "X is now the party leader."
- "Experience is now shared evenly." / "Experience is now shared by level."
- "The party was disbanded."
- "X opened a new <map>." (#1104): to every other online member once the leader X has arrived in a new instance of
  the party's, `<map>` being the map's name; see section 6.

## 6. Map entry

While in a party, a portal (`CMSG_ENTER_MAP`) to a **Normal** map leads to the party's instance of that map;
the first member through builds it, and members entering at once share it. Towns are unchanged, and a
character in no party still gets its own instance.

`SMapTransitionPacket.Result` (`MapTransitionResult`) gains one value:
- `InstanceFull` (6): the party's instance holds `min(MaxPartySize, the map's MaxPlayers)` players already.

A party that ends (or a character that leaves it) while the instance is being built is answered
`MapNotFound` (1).

**A new party instance (#1104).** `CEnterMapPacket.Choice` `NewParty` asks for a new instance of the map for the party,
apart from its current one ([choosing an instance](instanced-maps.md#choosing-an-instance)). Only the leader may ask;
`SMapTransitionPacket.Result` gains, among the choice's answers:
- `NotPartyLeader` (11): a member who is not the leader asked.
- `NotInParty` (12): a character in no party asked.

Once the leader has arrived in it, the new instance is the party's default, the one every member's plain entry takes,
and every other online member gets "X opened a new <map>." (section 5). Members already inside the old instance stay;
any member may go back to it through the portal's menu (`Existing`), which leaves the default where it is.

**Login always lands in a town**, never back in a party's instance. A member who logs out inside it walks back
in through the portal.

## 7. Loot and experience

**Loot.** `LootDropDto.OwnerCharacterId` (field 6) names the one character a drop is reserved for until
`FreeForAllAt` (field 7, UTC ticks); absent means anyone may take it now. In a party's instance each drop is
reserved for one eligible member drawn at random, so one kill's drops can go to different members. A client
shows "reserved for X" from the roster's names, and a pickup of someone else's drop before `FreeForAllAt` is
refused `NotYours` as before. Solo instances reserve every drop for their owner, and towns drop free for all.
A magic, rare or unique drop lies on the ground unidentified, the same for every member (no name, its implicit lines
only), and whoever picks it up gets it unidentified (#1179,
[identification](inventory-and-saves.md#identification-at-the-resonance-workbench-1179)); a common drop is identified.

Eligible for a kill: the killer, and every member in the same instance (alive or dead) who is in the
creature's fight or within 60 m of it, unless in a leave countdown. A killer in a leave countdown shares
nothing.

**Experience.** Shared among the eligible members by the party's mode (`ExperienceMode` on the roster):
- A member 5 or more levels above the creature gets nothing and is not counted. This holds **solo too**.
- One counted member: all of it, no bonus.
- Several: the pool is the creature's experience × (1 + 0.10 × (n − 1)).
  - `Even` (default): each gets pool / n, rounded down.
  - `LevelWeighted`: each gets pool × its level / the sum of the counted levels, rounded down.
- Each share is then scaled by the map's level band for that member, as solo experience is.

Examples (creature experience 100 unless noted):

| Creature level | Members' levels | Mode | Each gets |
|---|---|---|---|
| 10 | 10 | any | 100 (solo, no bonus) |
| 16 | 10, 10, 20 | Even | 40, 40, 40 (120 / 3) |
| 16 | 10, 10, 20 | LevelWeighted | 30, 30, 60 (120 × level / 40) |
| 10 | 10, 10 (66 experience) | Even | 36, 36 (72.6 / 2, rounded down) |
| 10 | 14, 15 | Even | 100, 0 (15 is 5 above; 14 is counted alone) |
| 10 | 15 (solo) | any | 0 |

The mode can be switched by the leader at any time outside combat, takes effect on the next kill, and then
cannot be switched again for 60 s.

## 8. Countdown and scaling lines

**Leave countdown.** A character who stops being a member (leaves, is kicked, or the party disbands) while
inside the party's instance has 60 s before the server moves it to the respawn town (alive, or revived if
dead). The lines, on `System`:
- at once: "You left the party. Returning to town in 60 seconds." (or "You were removed from the party. ..."
  or "The party was disbanded. ...");
- then "Returning to town in 30 seconds.", and the same at 10, 5, 4, 3, 2, and "Returning to town in 1 second.".

The move itself is an ordinary map transition (`SMapTransitionPacket`, then the town's chunk layout). The
countdown ends silently when the character is invited back into the same party and accepts, leaves the
instance itself (a portal, a respawn in town) or logs out. During it the character shares no kills.

**Health scaling.** In a party's instance every creature's maximum health is its base × (1 + 0.6 × (players
− 1)), counting every character in the instance, dead or counting down included: 160 % with two, 220 % with
three, 400 % with six. Current health keeps its share of the maximum; dead creatures are not changed. On each
change every player in the instance gets one line:
- "Kaela has entered. Creatures now have 220% health (3 players)."
- "Kaela has left. Creatures now have 160% health (2 players)."
- "Creatures now have 220% health (3 players)." when several players came or went in the same tick.

The new maximum and current health reach the client on the creature's ordinary state updates. Damage,
armour, experience and loot are not scaled.

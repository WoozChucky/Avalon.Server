# World maintenance and admission design

## Intent

An operator can close one world to players from the admin API or from that world's in-game Admin command, announce maintenance, and reopen it explicitly. A closing world admits no new non-admin players. Players already in the world receive a five-minute warning sequence and then disconnect through the normal save and despawn path. Admin accounts can enter and remain connected to verify the world. Maintenance survives a process restart. Auth shows truthful availability, and the world enforces admission even if auth has already issued a key.

The world startup listener race is tracked separately in [#665](https://github.com/WoozChucky/Avalon.Server/issues/665). This design assumes its readiness fix when specifying when the world publishes an online heartbeat. The leave-character exchange in [#663](https://github.com/WoozChucky/Avalon.Server/issues/663) keeps an authenticated world connection open; maintenance also gates a new character selection on that connection.

## State model

Store operator intent in the auth database on each world: `MaintenanceEnabled`, `MaintenanceRevision`, and `MaintenanceDeadlineUtc`. The revision increases only for a real transition. Enabling maintenance sets a deadline using the selected grace period; disabling it clears the deadline. A second enable while maintenance is active returns the existing revision and deadline without extending the countdown. A second disable is likewise a no-op. Serialize concurrent changes in the database so the last committed transition wins and revisions order notifications.

The world publishes a ready heartbeat to Redis once the world load, subscriptions, listener, and tick loop are ready. Refresh it every second with a five-second TTL only while the tick loop continues making progress. Remove it on graceful stop; expiry covers a crash or stalled tick. A missing or unreadable heartbeat means offline for admission and display. The heartbeat reports availability, not operator intent. Assume one active process per world ID; the heartbeat is not a leader election mechanism.

The public `WorldStatus` remains `Maintenance`, `Online`, or `Offline`, but is derived: maintenance takes precedence; otherwise a fresh ready heartbeat means online and no heartbeat means offline. The admin API also returns the underlying maintenance setting, deadline, and observed readiness so an offline world in maintenance is distinguishable from a running one. Auth's world list uses the derived status for worlds the account may see. Existing world access-level visibility rules remain in force.

Migrate an existing `WorldStatus.Maintenance` row to `MaintenanceEnabled = true` with a five-minute deadline from migration time; migrate `Online` and `Offline` rows to `false` with no deadline. Remove the stored `WorldStatus` column and remove `Status` from world create and general update requests. Clients may still read the derived `Status` field. New worlds start open but display offline until a ready heartbeat exists. Neither an API caller nor a seed row can write `Online` or `Offline` directly.

## Operator controls

Add an Admin-authorized `POST /world/{id}/maintenance` action with an optional integer grace period in minutes (default 5, range 1–60), a matching `DELETE /world/{id}/maintenance`, and `GET /world/{id}/maintenance` for the maintenance setting, deadline, revision, and observed readiness. These actions work while the world process is down. Keep ordinary world metadata updates separate from maintenance transitions.

Add `/maintenance on [minutes]`, `/maintenance off`, and `/maintenance status` to the world chat commands. Require the actual `AccountAccessLevel.Admin` flag, rather than the broader `AccessLevels.Admin` mask that also includes Console. The API and command call the same maintenance transition service and return the committed revision, state, and deadline to the operator. Log the actor, world ID, prior state, new state, and revision; do not log secrets. The command can be used only by an Admin already in that world. A failed database write reports failure and does not change the local state.

After a successful commit, publish the world ID and revision on a Redis channel. The world reads the authoritative row and applies only a newer revision; notification contents alone cannot change its state. The command's own world instance also applies the committed result immediately. Database reconciliation every five seconds covers a missed notification or Redis reconnect. A notification failure does not roll back a committed change; the operator receives the committed result and the delivery problem is logged for repair/retry.

## Admission

Auth checks the world row and ready heartbeat after the existing account and world-access checks, before reserving an in-world session slot or issuing a world key. For a visible world in maintenance, non-admin accounts receive a new append-only `WorldSelectResult.Maintenance` result. A world without a ready heartbeat is unavailable to all accounts. Unknown and access-restricted worlds keep the existing indistinguishable `WorldUnavailable` result. Admin accounts may select a maintenance world if it is ready and they otherwise satisfy the world's access rule. Staff bypass checks test the Admin flag, not an ordinal comparison.

The world checks the authoritative maintenance setting and account access at key exchange before accepting a session. A key issued just before the transition cannot bypass maintenance. It checks again for character selection on an already-authenticated world connection, including one returned to character selection by #663, and at the final pending-spawn boundary. A non-admin selection or pending spawn begun before the transition must not enter the map after the transition commits. A refused pre-entry connection receives an explicit maintenance disconnect reason and closes cleanly; no partial select or orphaned online character remains. Existing non-admin characters already in-game may continue during the grace period, but cannot use #663 to leave and select another character. A non-admin connection idle at character selection is closed at the deadline; an attempted selection is refused and closed immediately. Admin connections are exempt from admission and deadline disconnects.

The server must retain a TCP path for Admin connections during maintenance; the decision can only be made after account identity is known. The world server's admission checks, rather than a stopped listener, provide the authoritative maintenance gate. Add a distinct `DisconnectReason.Maintenance` for the world-side rejection and final disconnect. The client should display a maintenance message for the auth selection result and disconnect reason.

If the authoritative database cannot be read during an admission check, refuse a new non-admin admission. If account access or world identity cannot be verified, refuse all admissions. The world never treats a cached `Open` value as permission to bypass a failed authoritative check. Existing sessions are not disconnected merely because a read or heartbeat temporarily fails.

## Announcements and deadline

The first enable commits a UTC deadline. The world sends an encrypted system chat broadcast to in-game sessions when maintenance starts, at three minutes, one minute, and thirty seconds remaining, then at every integer second from ten through zero. For a shorter custom grace period, skip thresholds already past; still announce immediately and send each applicable final-second warning. Messages state the time remaining and that non-admin players will be disconnected. Use the existing `System` chat sender. Admins can see the broadcasts but are not disconnected.

Drive warnings from the committed deadline, not from counting timer ticks. A delayed tick sends the most relevant current warning and skips stale thresholds rather than sending a backlog or extending the deadline. Under normal ticking, every second from ten through zero is announced once. At zero, enqueue the zero-second announcement before a maintenance disconnect packet and await close for non-admin sessions. Use the existing close, despawn, and save path; do not stop the world process. If maintenance is disabled before the deadline, cancel unsent warnings and the disconnect. If it is enabled again later, create a new revision and countdown.

On world restart, load the persisted maintenance setting before admitting players. Existing sessions are gone, so do not replay an old countdown; keep non-admin admission closed and allow Admin verification. A restarted world may publish a ready heartbeat while still in maintenance. The admin view then reports both `MaintenanceEnabled = true` and `Ready = true`.

## Failure and ordering rules

- The database is the source of truth for operator intent. Redis pub/sub accelerates delivery but is not authoritative. Revision checks discard old or duplicate notifications.
- A committed enable closes new admission immediately at authoritative gates even if the in-game announcement arrives later. Reconciliation eventually starts the countdown at the original deadline, not a new five-minute deadline.
- If the world learns of maintenance after its deadline, it sends the final notice and disconnects remaining non-admin sessions promptly.
- A failed disable leaves maintenance active. A successful disable cancels a local countdown as soon as the newer revision is applied; authoritative admission checks already see the open state.
- A ready-heartbeat refresh failure makes auth conservatively show offline after TTL expiry. It does not itself kick existing players. World key exchange already depends on Redis, and new admissions fail when required state cannot be verified.
- Graceful process shutdown retains its existing shutdown notice, connection close, and save drain behavior. Maintenance never marks the world process offline merely because players are being drained.

## Verification

Test persistent transitions and migration of legacy status values; idempotent and concurrent on/off requests; Admin-only API and chat authorization; revision ordering and missed notification reconciliation; heartbeat startup, expiry, and graceful stop; derived status in the API and auth list; auth refusal before world-key and slot creation; world key exchange, character selection after #663 leave, pending-spawn race, and Admin bypass; countdown threshold crossing, every second from ten to zero, cancellation, restart, and final packet-before-close ordering; and save/despawn completion on maintenance disconnect.

The client packet/schema work must preserve existing enum values and append the new maintenance result and disconnect reason. Client UI work must handle both responses and the system chat countdown. Do not fold the listener-order change from #665 into this feature.

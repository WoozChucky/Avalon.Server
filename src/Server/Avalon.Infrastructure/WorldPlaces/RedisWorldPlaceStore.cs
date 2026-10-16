using System.Collections.Concurrent;
using System.Globalization;
using Avalon.Common.GameAuth;
using Avalon.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Avalon.Infrastructure.WorldPlaces;

/// <summary>
/// <see cref="IWorldPlaceStore"/> in Redis (#967): every operation one Lua script, so "check the limit and take a place"
/// and "leave the line and take a place" are single steps. Time is the caller's <see cref="TimeProvider"/>, passed in as
/// Unix milliseconds. Single-instance Redis only: joining a line leaves the line of another world, which the script
/// names from the account's waiting key (<see cref="CacheKeys.WorldQueueWaiting"/>), not from KEYS.
/// <para>
/// Who holds a place is in the world's marks hash (<see cref="CacheKeys.WorldPlaceMarks"/>), five fields per account at
/// most. The field <c>{account}</c> is the place's kind: <c>h</c> (held after a drop), <c>t:{enqueuedAtMs}</c> (a turn
/// not yet used), <see cref="TicketMark"/> (a join ticket's hold, the last ticket to enter) or <c>r</c> (released by the
/// account's ticket for another world while its own ticket's hold still ran: counted until that hold ends, kept by no
/// further ticket, renewed by that ticket's redemption). The field
/// <c>{account}:s</c> is the fencing token of the session that owns the place's lease (<c>{fence}</c>), or of the session
/// that ended there (<c>e:{fence}</c>, a tombstone Redis expires one lease after it was written, <c>HPEXPIRE</c>, whether the
/// place was kept or removed); fencing tokens only grow for an account, so a smaller one is an older session's. The field
/// <c>{account}:f</c> is when the place became unclaimed (Unix ms): taken or kept by a join ticket, or left by a session's
/// end, with no session's activation or heartbeat since; past <see cref="WorldPlacesConfiguration.UnclaimedPlace"/> a
/// further ticket is not kept on it (<see cref="EnterAsync"/>). The field <c>{account}:l</c> (<c>1</c>) is a lockout: an
/// account refused <c>WORLD_FULL</c> when its unclaimed place lapsed is refused again until Redis expires it,
/// <see cref="GameAuthPolicy.WorldFullRetryAfterMin"/> after its place is free, so it cannot take back at once the place
/// it was released from; a place's removal leaves it to expire, and an activation removes it. The field
/// <c>{account}:p</c> is the fencing token of a session redeemed and not yet activated there, for one lease at most
/// (<c>HPEXPIRE</c>): while it lives, and no lockout does, the place does not lapse. One more field, <c>released</c>, is the time the world's held
/// places were last released (<see cref="ReleaseHeldAsync"/>).
/// </para>
/// </summary>
public sealed class RedisWorldPlaceStore(IReplicatedCache cache, IOptions<WorldPlacesConfiguration> options,
    IOptions<GameWorkloadConfiguration> workloads, TimeProvider clock, PlaceLedgerOutage outage) : IWorldPlaceStore
{
    private const long WaitingKeySeconds = 86_400;

    /// <summary>
    /// The mark of a join ticket's hold on a place: <c>j:{untilMs}:{ticket}</c>, the ticket by the id (<c>N</c> format)
    /// of the session it becomes, so a withdrawal or an activation only ever touches its own ticket's hold. A ticket that
    /// took a place held after a drop carries <c>:h</c> after its id (<paramref name="wasHeld"/>), so its withdrawal holds
    /// the place again rather than removing it.
    /// </summary>
    public static string TicketMark(long untilMs, Guid ticket, bool wasHeld = false) =>
        string.Create(CultureInfo.InvariantCulture, $"j:{untilMs}:{ticket:N}{(wasHeld ? ":h" : "")}");

    /// <summary>
    /// What every script reads a place's holders with: a ticket's hold (its end and id) from the kind field, the owning
    /// session's fencing token from the <c>{account}:s</c> field (<c>{fence}</c> while it is live, <c>e:{fence}</c> once it
    /// ended), and the removal of a place with both its fields. An ended session's field is a tombstone: written only by
    /// <c>tombstone</c>, which always sets it to expire one lease from now on Redis's clock (<c>HPEXPIRE</c>), so it refuses that
    /// session's late renewals whether or not the place is still there, and never outlives them. (A plain <c>HSET</c> of a
    /// newer session's live fence clears the expiry, as it should: the field then lives with the place.) A refused expiry
    /// (a tombstone's, a pending marker's, a lockout's) deletes the field and sets <c>refused</c>, which the
    /// scripts that expire fields return beside their result, so it is logged.
    /// </summary>
    private const string Holders = """
        local refused = 0
        -- A ticket's hold: its end, its id, and whether the place was held after a drop when the ticket took it.
        local function ticket(mark)
          if mark then
            local ends, id, was = string.match(mark, '^j:(%d+):(%x+)(:?h?)$')
            if ends then return tonumber(ends), id, was == ':h' end
          end
          return nil, nil, false
        end
        local function ticket_mark(ends, id, was)
          if was then return 'j:' .. ends .. ':' .. id .. ':h' end
          return 'j:' .. ends .. ':' .. id
        end
        local function owner(marks, account)
          local value = redis.call('HGET', marks, account .. ':s')
          if not value then return nil, false end
          if string.sub(value, 1, 2) == 'e:' then return tonumber(string.sub(value, 3)), true end
          return tonumber(value), false
        end
        -- The place's fields go with it (its kind, since when no session claims it, a pending session's marker), but an
        -- ended session's tombstone, which expires on its own.
        local function forget_fields(marks, account)
          redis.call('HDEL', marks, account, account .. ':f', account .. ':p')
          local _, ended = owner(marks, account)
          if not ended then redis.call('HDEL', marks, account .. ':s') end
        end
        local function forget_place(places, marks, account)
          redis.call('ZREM', places, account)
          forget_fields(marks, account)
        end
        -- Since when no session claims the place (Unix ms), recorded once: the earliest moment stands until a session's
        -- activation or heartbeat claims it again (Renew), or the place goes.
        local function unclaimed(marks, account, now)
          local since = redis.call('HGET', marks, account .. ':f')
          redis.call('HSET', marks, account .. ':f', since or now)
        end
        -- A field that expires ttl ms from now on Redis's own clock, so no clock skew between identity and Redis can cut
        -- it short. Should the expiry fail (HPEXPIRE refused, or a Redis before 7.4), the field is removed rather than
        -- left without one, whatever it held before: never a field that lasts forever. The refusal is returned, and
        -- logged by the caller.
        local function expire_field(marks, field, ttl)
          local armed = redis.pcall('HPEXPIRE', marks, ttl, 'FIELDS', 1, field)
          if type(armed) == 'table' and armed.err then
            redis.call('HDEL', marks, field)
            refused = 1
          end
        end
        -- An ended session's tombstone, expiring one lease (ttl ms) after it is written (an earlier tombstone too is
        -- rearmed).
        local function tombstone(marks, account, fence, ttl)
          redis.call('HSET', marks, account .. ':s', 'e:' .. fence)
          expire_field(marks, account .. ':s', ttl)
        end

        """;

    /// <summary>The helpers every script that sweeps starts with. The line keys of another world are spelled as in CacheKeys.</summary>
    private const string Functions = Holders + """
        local function forget(key, world)
          local waiting = redis.call('GET', key)
          if waiting and string.sub(waiting, 1, #world + 1) == world .. ':' then redis.call('DEL', key) end
        end
        local function enqueued_at(key, now)
          local waiting = redis.call('GET', key)
          return (waiting and string.match(waiting, ':(%d+)$')) or now
        end
        local function timeout(position, front, front_ms, back_ms)
          if position <= front then return front_ms end
          return back_ms
        end
        -- A place that expires unclaimed past the horizon was kept by tickets alone to the end (no session claimed it
        -- for that long, and a drop's held place lasts the held grace, which the horizon covers): its account is locked
        -- out as at a lapse (lockout ms), so it cannot let the place run out and take it back at once as a newcomer.
        local function sweep(places, marks, queue, checkins, now, prefix, world, horizon, lockout)
          local turns = 0
          local expired = redis.call('ZRANGEBYSCORE', places, '-inf', now)
          for _, account in ipairs(expired) do
            local mark = redis.call('HGET', marks, account)
            if mark and string.sub(mark, 1, 2) == 't:' then turns = turns + 1 end
            local since = tonumber(redis.call('HGET', marks, account .. ':f'))
            if since and tonumber(now) - since >= tonumber(horizon) then
              redis.call('HSET', marks, account .. ':l', '1')
              expire_field(marks, account .. ':l', lockout)
            end
            forget_fields(marks, account)
          end
          if #expired > 0 then redis.call('ZREMRANGEBYSCORE', places, '-inf', now) end
          local silent = redis.call('ZRANGEBYSCORE', checkins, '-inf', now)
          for _, account in ipairs(silent) do
            redis.call('ZREM', queue, account)
            forget(prefix .. account, world)
          end
          if #silent > 0 then redis.call('ZREMRANGEBYSCORE', checkins, '-inf', now) end
          return turns, #silent
        end
        -- Whether the account has a place here (present) and whether it lapsed. A place another world's ticket released
        -- (r) is counted but not the account's to keep, so not present. A present place lapsed when no session has claimed
        -- it for the horizon (only tickets kept it), unless a session redeemed there is pending (its marker lives and no
        -- lockout does). Only a present place that has not lapsed is the account's own: a join keeps it, and a check-in
        -- answers it as the account's turn.
        local function standing(places, marks, account, now, horizon)
          local present = redis.call('ZSCORE', places, account) and redis.call('HGET', marks, account) ~= 'r'
          local lapsed = false
          if present then
            local since = tonumber(redis.call('HGET', marks, account .. ':f'))
            local pending = redis.call('HGET', marks, account .. ':p') and not redis.call('HGET', marks, account .. ':l')
            lapsed = since ~= nil and now - since >= tonumber(horizon) and not pending
          end
          return present, lapsed
        end
        -- A turn's mark: when the account joined the line (Unix ms) and, for a turn given over a place the account still
        -- had counted (released by its ticket for another world, or lapsed and kept for its last ticket's hold), that
        -- place's end and since when it was unclaimed (0 for no record), so a turn given back returns the place to what it
        -- was. Nil for any other mark.
        local function turn(mark)
          if not mark then return nil end
          local enqueued, ends, since = string.match(mark, '^t:(%d+):(%d+):(%d+)$')
          if enqueued then return tonumber(enqueued), tonumber(ends), tonumber(since) end
          enqueued = string.match(mark, '^t:(%d+)$')
          if enqueued then return tonumber(enqueued), nil, nil end
          return nil
        end
        -- A turn not used, given back (a leave, or the account's ticket or turn for another world): the place it was given
        -- over, while that place's own end is still ahead, is counted again to that end as released (r), kept by no further
        -- ticket and renewed by its old ticket's redemption, with its unclaimed time; otherwise the place goes.
        local function give_back(places, marks, account, now)
          local _, ends, since = turn(redis.call('HGET', marks, account))
          if ends and ends > now then
            redis.call('ZADD', places, 'XX', ends, account)
            redis.call('HSET', marks, account, 'r')
            if since > 0 then
              redis.call('HSET', marks, account .. ':f', since)
            else
              redis.call('HDEL', marks, account .. ':f')
            end
          else
            forget_place(places, marks, account)
          end
        end
        -- One reservation per account across worlds: a ticket admitted or a turn given here releases what the account
        -- holds in the other worlds (KEYS from first, places and marks in pairs) without a live session owning it: a place
        -- only a ticket holds, and an unused turn. Never a place held after a drop, nor one a ticket returned to from a drop
        -- (j:...:h), nor one a live session owns. While a ticket-only place's hold still runs the ticket may still be
        -- redeemed: the place stays counted to the hold's end, marked released (r), so no further ticket keeps it, and that
        -- redemption renews a place that never left the count rather than putting one back over the limit. A turn is given
        -- back (give_back).
        local function release_elsewhere(first, account, now)
          for i = first, #KEYS, 2 do
            local mark = redis.call('HGET', KEYS[i + 1], account)
            local current, ended = owner(KEYS[i + 1], account)
            if not current or ended then
              local held, id, was = ticket(mark)
              if id and not was then
                if held > now then
                  redis.call('ZADD', KEYS[i], 'XX', held, account)
                  redis.call('HSET', KEYS[i + 1], account, 'r')
                else
                  forget_place(KEYS[i], KEYS[i + 1], account)
                end
              elseif turn(mark) then
                give_back(KEYS[i], KEYS[i + 1], account, now)
              end
            end
          end
        end

        """;

    // KEYS: 1 places, 2 marks, 3 queue, 4 checkins, 5 arrivals, 6 the account's waiting key, then places and marks of
    //       each other world.
    // ARGV: 1 account, 2 now, 3 until, 4 limit ('' for none), 5 staff ('1'/'0'), 6 queue capacity, 7 front positions,
    //       8 front timeout ms, 9 back timeout ms, 10 waiting-key prefix, 11 world id, 12 waiting-key lifetime s,
    //       13 the ticket's id (N format), 14 the unclaimed horizon in ms, 15 the lockout of a lapsed place refused in ms.
    // Returns {outcome, source, position, waited ms, turns expired, waiters timed out, 1 when a field's expiry was refused,
    // 1 when refused for a lockout}.
    private const string Enter = Functions + """
        local account, now = ARGV[1], tonumber(ARGV[2])
        local turns, silent = sweep(KEYS[1], KEYS[2], KEYS[3], KEYS[4], ARGV[2], ARGV[10], ARGV[11], ARGV[14], ARGV[15])
        -- Every place a ticket is admitted to is never shortened (a held place's grace, a turn, an owner's lease), and is
        -- the ticket's to hold. The owner field is left as it is: on a new place it can only be an ended session's
        -- tombstone, which keeps refusing that session's late renewals until it expires. A ticket admitted here releases
        -- what the account holds elsewhere with no session (release_elsewhere).
        local function admit(source, waited, was)
          redis.call('ZADD', KEYS[1], 'GT', ARGV[3], account)
          redis.call('HSET', KEYS[2], account, ticket_mark(ARGV[3], ARGV[13], was))
          unclaimed(KEYS[2], account, ARGV[2])
          release_elsewhere(7, account, now)
          return {0, source, 0, waited, turns, silent, refused, 0}
        end
        local function leave_line()
          redis.call('ZREM', KEYS[3], account)
          redis.call('ZREM', KEYS[4], account)
          forget(KEYS[6], ARGV[11])
        end
        -- A place no session has claimed for the horizon (only tickets have kept it) is not kept again: the request goes
        -- through the capacity step as a newcomer's, its place still counted. Admitted, the place is its own again with a
        -- new horizon; refused or queued, the place is released, as a newcomer has none, but not while its last ticket's
        -- hold runs: that ticket may still be redeemed, and its session must find the place it was counted for, not put
        -- it back over the limit. A place a session redeemed and not yet activated (its pending marker, one lease at most)
        -- never lapses.
        -- A place another world's ticket released (r) is counted but not the account's to keep: it asks as a newcomer.
        local present, lapsed = standing(KEYS[1], KEYS[2], account, now, ARGV[14])
        local function hold_end()
          local held = ticket(redis.call('HGET', KEYS[2], account))
          if held and held > now then return held end
          return nil
        end
        local function readmit()
          if lapsed then redis.call('HDEL', KEYS[2], account .. ':f') end
          return admit(0, 0)
        end
        local function release()
          if lapsed and not hold_end() then forget_place(KEYS[1], KEYS[2], account) end
        end
        if present and not lapsed then
          -- A place of its own needs no position: an account also in the line leaves it.
          if redis.call('ZRANK', KEYS[3], account) then leave_line() end
          local mark = redis.call('HGET', KEYS[2], account)
          if mark == 'h' then return admit(2, 0, true) end
          local enqueued = turn(mark)
          if enqueued then return admit(3, now - enqueued) end
          -- Kept: never shortened, and held by the last ticket to enter, to the later of its end and the hold already
          -- there, so the withdrawal of a ticket that entered before it never matches.
          redis.call('ZADD', KEYS[1], 'GT', ARGV[3], account)
          local ends = tonumber(ARGV[3])
          local held, _, was = ticket(mark)
          if held and held > ends then ends = held end
          redis.call('HSET', KEYS[2], account, ticket_mark(string.format('%d', ends), ARGV[13], was))
          unclaimed(KEYS[2], account, ARGV[2])
          release_elsewhere(7, account, now)
          return {0, 1, 0, 0, turns, silent, refused, 0}
        end
        local rank = redis.call('ZRANK', KEYS[3], account)
        local limit = tonumber(ARGV[4])
        if ARGV[5] == '1' or not limit then
          if rank then leave_line() end
          return readmit()
        end
        local taken = redis.call('ZCARD', KEYS[1])
        local front, front_ms, back_ms = tonumber(ARGV[7]), tonumber(ARGV[8]), tonumber(ARGV[9])
        if rank then
          if rank < limit - taken then
            local waited = now - tonumber(enqueued_at(KEYS[6], ARGV[2]))
            leave_line()
            if lapsed then redis.call('HDEL', KEYS[2], account .. ':f') end
            return admit(3, waited)
          end
          release()
          redis.call('ZADD', KEYS[4], now + timeout(rank + 1, front, front_ms, back_ms), account)
          return {1, 0, rank + 1, 0, turns, silent, refused, 0}
        end
        -- Refused at its lapse a moment ago: refused again until the lockout expires, so it cannot take back at once
        -- the place it was released from, ahead of the players told to come back in 45 to 75 s.
        if redis.call('HGET', KEYS[2], account .. ':l') then return {2, 0, 0, 0, turns, silent, refused, 1} end
        local waiting = redis.call('ZCARD', KEYS[3])
        if waiting == 0 and taken < limit then return readmit() end
        release()
        if waiting >= tonumber(ARGV[6]) then
          -- Refused at its lapse: locked out for the shortest Retry-After the others are told, counted from when its
          -- place is free (the place's own end, when it is kept for its last ticket's hold), so the others have that long
          -- to take it.
          if lapsed then
            local wait = tonumber(ARGV[15])
            local kept = tonumber(redis.call('ZSCORE', KEYS[1], account))
            if kept and kept > now then wait = wait + kept - now end
            redis.call('HSET', KEYS[2], account .. ':l', '1')
            expire_field(KEYS[2], account .. ':l', string.format('%d', wait))
          end
          return {2, 0, 0, 0, turns, silent, refused, 0}
        end
        local previous = redis.call('GET', KEYS[6])
        local other = previous and string.match(previous, '^(%d+):')
        if other and other ~= ARGV[11] then
          redis.call('ZREM', 'world:' .. other .. ':queue', account)
          redis.call('ZREM', 'world:' .. other .. ':queue:checkins', account)
        end
        redis.call('ZADD', KEYS[3], redis.call('INCR', KEYS[5]), account)
        local position = redis.call('ZRANK', KEYS[3], account) + 1
        redis.call('ZADD', KEYS[4], now + timeout(position, front, front_ms, back_ms), account)
        redis.call('SET', KEYS[6], ARGV[11] .. ':' .. ARGV[2], 'EX', ARGV[12])
        return {1, 0, position, 0, turns, silent, refused, 0}
        """;

    // KEYS: 1 places, 2 marks of the world, then places and marks of each other world (an activation only).
    // ARGV: 1 account, 2 until (the lease), 3 '1' for an activation, 4 the session's fencing token, 5 the session's id
    //       (N format), 6 now, 7 a tombstone's lifetime in ms (one lease).
    // Returns {1 when the session owns the place now, 0 when it changed nothing; 1 when a tombstone's expiry was refused}.
    private const string Renew = Holders + """
        local account, expiry, fence, now = ARGV[1], tonumber(ARGV[2]), tonumber(ARGV[4]), tonumber(ARGV[6])
        local current, ended = owner(KEYS[2], account)
        -- A session older than the place's owner renews nothing, nor does a session that ended there, or older than one
        -- that did (whether the place was kept or removed: the tombstone outlives the place by one lease).
        if current and (current > fence or (ended and current >= fence)) then return {0, refused} end
        -- A session claims the place at its activation, and at each heartbeat of the session that owns it; not at a
        -- redemption alone, which a session never activated could repeat to keep a place without playing. A redemption
        -- marks its session pending there, for one lease at most, so its place does not lapse before the activation. An
        -- activation also lifts a lockout: the account is playing.
        if ARGV[3] == '1' then
          redis.call('HDEL', KEYS[2], account .. ':f', account .. ':p', account .. ':l')
        elseif current == fence and not ended then
          redis.call('HDEL', KEYS[2], account .. ':f', account .. ':p')
        else
          redis.call('HSET', KEYS[2], account .. ':p', ARGV[4])
          expire_field(KEYS[2], account .. ':p', ARGV[7])
        end
        local mark = redis.call('HGET', KEYS[2], account)
        local held, id = ticket(mark)
        if held and ARGV[3] == '1' and id == ARGV[5] then
          -- The activation spends its own ticket's hold, and only its own.
          redis.call('HDEL', KEYS[2], account)
          held = nil
        elseif mark and not held then
          redis.call('HDEL', KEYS[2], account)
        end
        if held and held > now and held > expiry then expiry = held end
        redis.call('ZADD', KEYS[1], expiry, account)
        redis.call('HSET', KEYS[2], account .. ':s', ARGV[4])
        for i = 3, #KEYS, 2 do
          -- A session is one per account: its place in another world goes, but for a ticket's hold still running there,
          -- and the older sessions there are ended, place or not (lost, swept), so a late renewal of one cannot bring a
          -- place back.
          local other = owner(KEYS[i + 1], account)
          if not other or other <= fence then
            if redis.call('ZSCORE', KEYS[i], account) then
              local hold = ticket(redis.call('HGET', KEYS[i + 1], account))
              if hold and hold > now then
                redis.call('ZADD', KEYS[i], 'GT', hold, account)
              else
                redis.call('ZREM', KEYS[i], account)
                redis.call('HDEL', KEYS[i + 1], account, account .. ':f', account .. ':p')
              end
            end
            tombstone(KEYS[i + 1], account, ARGV[4], ARGV[7])
          end
        end
        return {1, refused}
        """;

    // KEYS: 1 places, 2 marks. ARGV: 1 account, 2 '1' to hold, 3 held until, 4 now, 5 the ending session's fencing token,
    //       6 a tombstone's lifetime in ms (one lease).
    // Returns {1 when the place was the session's to end, 0 when a newer session owns it, this session's end was already
    // taken, or the place no longer exists; 1 when the tombstone's expiry was refused}.
    private const string End = Holders + """
        local account, now, fence = ARGV[1], tonumber(ARGV[4]), tonumber(ARGV[5])
        local current, ended = owner(KEYS[2], account)
        if current and current > fence then return {0, refused} end
        -- The same session's end again (a retry): the first one decided the place, and a repeat never extends a hold.
        if ended and current == fence then return {0, refused} end
        -- Whatever the end does to the place, the session is ended there: its late renewals change nothing, and it is
        -- pending there no more.
        tombstone(KEYS[2], account, ARGV[5], ARGV[6])
        redis.call('HDEL', KEYS[2], account .. ':p')
        -- An end never creates a place: one lost or removed meanwhile stays gone, and so does one whose time has passed,
        -- swept or not.
        local score = redis.call('ZSCORE', KEYS[1], account)
        if not score then return {0, refused} end
        if tonumber(score) <= now then
          forget_place(KEYS[1], KEYS[2], account)
          return {0, refused}
        end
        local held = ticket(redis.call('HGET', KEYS[2], account))
        if held and held <= now then held = nil end
        -- An account locked out at its place's lapse gets no held grace: the grace would outlast its lockout, and the
        -- place is the one the others are owed.
        if ARGV[2] == '1' and not redis.call('HGET', KEYS[2], account .. ':l') then
          redis.call('ZADD', KEYS[1], 'GT', ARGV[3], account)
          -- A ticket's hold still running keeps its mark, so the session it becomes can renew the place.
          if not held then redis.call('HSET', KEYS[2], account, 'h') end
          unclaimed(KEYS[2], account, ARGV[4])
        elseif held then
          redis.call('ZADD', KEYS[1], 'GT', held, account)
          unclaimed(KEYS[2], account, ARGV[4])
        else
          forget_place(KEYS[1], KEYS[2], account)
        end
        return {1, refused}
        """;

    // KEYS: 1 places, 2 marks. ARGV: 1 account, 2 the withdrawn ticket's id (N format), 3 a tombstone's lifetime in ms (one
    //       lease).
    // Returns {1 when the hold was the ticket's and is given back, 0 when another ticket entered since; 1 when the
    // tombstone's expiry was refused}.
    private const string Withdraw = Holders + """
        local _, id, was = ticket(redis.call('HGET', KEYS[2], ARGV[1]))
        if id ~= ARGV[2] then return {0, refused} end
        local current, ended = owner(KEYS[2], ARGV[1])
        if was and (not current or ended) then
          -- The ticket took a place held after a drop: it is held again, to the end it has, never removed.
          redis.call('HSET', KEYS[2], ARGV[1], 'h')
          return {1, refused}
        end
        redis.call('HDEL', KEYS[2], ARGV[1])
        if not current or ended then
          redis.call('ZREM', KEYS[1], ARGV[1])
          redis.call('HDEL', KEYS[2], ARGV[1] .. ':f')
        end
        -- An ended session's tombstone stays, rearmed for one lease: the place it was kept for is gone.
        if ended then tombstone(KEYS[2], ARGV[1], string.format('%d', current), ARGV[3]) end
        return {1, refused}
        """;

    // KEYS: 1 places, 2 marks. ARGV: 1 now, 2 the window in ms. The marks hash's field 'released' (never an account's) is
    // the last release's time.
    // Returns how many held places were released.
    private const string ReleaseHeld = Holders + """
        local now = tonumber(ARGV[1])
        local last = tonumber(redis.call('HGET', KEYS[2], 'released'))
        if last and now >= last and now - last < tonumber(ARGV[2]) then return 0 end
        redis.call('HSET', KEYS[2], 'released', ARGV[1])
        local released = 0
        for _, account in ipairs(redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', '+inf')) do
          if redis.call('HGET', KEYS[2], account) == 'h' then
            forget_place(KEYS[1], KEYS[2], account)
            released = released + 1
          end
        end
        return released
        """;

    // KEYS: 1 places, 2 marks. ARGV: 1 account, 2 until, 3 the replayed ticket's id (N format).
    // Returns 1 when the place was there to extend, 0 when there is none: a replay never creates one.
    private const string Rejoin = Holders + """
        if not redis.call('ZSCORE', KEYS[1], ARGV[1]) then return 0 end
        local mark = redis.call('HGET', KEYS[2], ARGV[1])
        -- A place another world's ticket released is no ticket's to extend.
        if mark == 'r' then return 0 end
        redis.call('ZADD', KEYS[1], 'GT', ARGV[2], ARGV[1])
        if mark == 'h' or (mark and string.sub(mark, 1, 2) == 't:') then return 1 end
        local ends = tonumber(ARGV[2])
        local held, _, was = ticket(mark)
        if held and held > ends then ends = held end
        redis.call('HSET', KEYS[2], ARGV[1], ticket_mark(string.format('%d', ends), ARGV[3], was))
        return 1
        """;

    // KEYS: 1 places, 2 marks, 3 queue, 4 checkins, 5 the account's waiting key, then places and marks of each other
    //       world (a turn given releases what the account holds there with no session).
    // ARGV: 1 account, 2 now, 3 limit ('' for none), 4 available ('1'/'0'), 5 front positions, 6 front timeout ms,
    //       7 back timeout ms, 8 the turn's end, 9 waiting-key prefix, 10 world id, 11 the unclaimed horizon in ms, 12 the
    //       lockout in ms.
    // Returns {state, position, turns expired, waiters timed out, 1 when a field's expiry was refused}.
    private const string CheckIn = Functions + """
        local account, now = ARGV[1], tonumber(ARGV[2])
        local turns, silent = sweep(KEYS[1], KEYS[2], KEYS[3], KEYS[4], ARGV[2], ARGV[9], ARGV[10], ARGV[11], ARGV[12])
        -- The account's turn at once only for a place of its own, which its join keeps (a turn taken, a place held after
        -- a drop, its session's, a ticket's hold not lapsed). A place that is not (released by another world's ticket,
        -- or lapsed and kept only for its last ticket's hold) is counted but is no turn: the account waits its rank.
        local present, lapsed = standing(KEYS[1], KEYS[2], account, now, ARGV[11])
        local rank = redis.call('ZRANK', KEYS[3], account)
        local function leave_line()
          redis.call('ZREM', KEYS[3], account)
          redis.call('ZREM', KEYS[4], account)
          forget(KEYS[5], ARGV[10])
        end
        if present and not lapsed then
          -- A place of its own needs no position: an account also in the line leaves it.
          if rank then leave_line() end
          -- An unused turn while the world is not available is kept, its window starting again at each check-in, so a
          -- turn given just before the world went away is not lost while the line is paused.
          if ARGV[4] ~= '1' and turn(redis.call('HGET', KEYS[2], account)) then
            redis.call('ZADD', KEYS[1], 'GT', ARGV[8], account)
            return {2, 1, turns, silent, refused}
          end
          return {3, 0, turns, silent, refused}
        end
        if not rank then return {0, 0, turns, silent, refused} end
        local limit = tonumber(ARGV[3])
        if ARGV[4] == '1' and (not limit or rank < limit - redis.call('ZCARD', KEYS[1])) then
          local enqueued = enqueued_at(KEYS[5], ARGV[2])
          leave_line()
          -- A turn given over a place the account still has counted (released by its ticket for another world, or lapsed
          -- and kept for its last ticket's hold) never shortens it (that ticket may still be redeemed), and records the
          -- place's end and unclaimed time, so a turn given back returns it to what it was. The turn is its own: no
          -- longer lapsed, so its join takes it.
          local mark = 't:' .. enqueued
          local kept = redis.call('ZSCORE', KEYS[1], account)
          if kept then
            local since = tonumber(redis.call('HGET', KEYS[2], account .. ':f')) or 0
            mark = mark .. ':' .. string.format('%d', tonumber(kept)) .. ':' .. string.format('%d', since)
          end
          redis.call('ZADD', KEYS[1], 'GT', ARGV[8], account)
          redis.call('HSET', KEYS[2], account, mark)
          redis.call('HDEL', KEYS[2], account .. ':f')
          release_elsewhere(6, account, now)
          return {3, 0, turns, silent, refused}
        end
        local position = rank + 1
        redis.call('ZADD', KEYS[4], now + timeout(position, tonumber(ARGV[5]), tonumber(ARGV[6]), tonumber(ARGV[7])), account)
        if ARGV[4] == '1' then return {1, position, turns, silent, refused} end
        return {2, position, turns, silent, refused}
        """;

    // KEYS: 1 places, 2 marks, 3 queue, 4 checkins, 5 the account's waiting key. ARGV: 1 account, 2 world id, 3 now.
    // Returns 1 when something was removed: a position, or an unused turn given back.
    private const string Leave = Functions + """
        local removed = redis.call('ZREM', KEYS[3], ARGV[1])
        redis.call('ZREM', KEYS[4], ARGV[1])
        forget(KEYS[5], ARGV[2])
        if turn(redis.call('HGET', KEYS[2], ARGV[1])) then
          give_back(KEYS[1], KEYS[2], ARGV[1], tonumber(ARGV[3]))
          removed = 1
        end
        return removed
        """;

    // KEYS: 1 places, 2 marks, 3 queue, 4 checkins. ARGV: 1 now, 2 waiting-key prefix, 3 world id, 4 the unclaimed horizon
    //       in ms, 5 the lockout in ms.
    // Returns {taken, queued, held, turns expired, waiters timed out, 1 when a field's expiry was refused}.
    private const string Count = Functions + """
        local turns, silent = sweep(KEYS[1], KEYS[2], KEYS[3], KEYS[4], ARGV[1], ARGV[2], ARGV[3], ARGV[4], ARGV[5])
        local held = 0
        for _, mark in ipairs(redis.call('HVALS', KEYS[2])) do
          if mark == 'h' then held = held + 1 end
        end
        return {redis.call('ZCARD', KEYS[1]), redis.call('ZCARD', KEYS[3]), held, turns, silent, refused}
        """;

    public async Task<PlaceDecision> EnterAsync(PlaceRequest request, CancellationToken cancellationToken)
    {
        WorldPlacesConfiguration c = options.Value;
        long until = Ms(request.Until);
        RedisResult[] r = await RunArrayAsync(Enter,
        [
            .. WorldKeys(request.WorldId), CacheKeys.WorldQueueArrivals(request.WorldId), CacheKeys.WorldQueueWaiting(request.AccountId),
            .. OtherWorldKeys(request.WorldId),
        ],
        [
            request.AccountId, Now, until, Limit(request.Limit), request.Staff ? "1" : "0",
            request.QueueCapacity, WorldPlacesConfiguration.FrontPositions, Ms(c.TimeoutAt(1)),
            Ms(c.TimeoutAt(WorldPlacesConfiguration.FrontPositions + 1)), CacheKeys.WorldQueueWaitingPrefix,
            (int)request.WorldId, WaitingKeySeconds, request.Ticket.ToString("N"), Ms(c.UnclaimedPlace),
            Ms(GameAuthPolicy.WorldFullRetryAfterMin),
        ], cancellationToken);
        if ((int)r[6] == 1) outage.ReportExpiryRefused();
        return new((PlaceOutcome)(int)r[0], (PlaceSource)(int)r[1], (int)r[2], TimeSpan.FromMilliseconds((long)r[3]),
            new((int)r[4], (int)r[5]), LockedOut: (int)r[7] == 1);
    }

    public async Task RenewAsync(ushort worldId, long accountId, long fence, Guid sessionId, DateTime until, bool activation,
        CancellationToken cancellationToken)
    {
        RedisKey[] keys = activation
            ? [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId), .. OtherWorldKeys(worldId)]
            : [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)];
        await RunTombstoningAsync(Renew, keys,
            [accountId, Ms(until), activation ? "1" : "0", fence, sessionId.ToString("N"), Now, s_tombstoneLifetime], cancellationToken);
    }

    public Task EndAsync(ushort worldId, long accountId, long fence, PlaceEnd end, DateTime heldUntil,
        CancellationToken cancellationToken) =>
        RunTombstoningAsync(End, [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)],
            [accountId, end == PlaceEnd.Hold ? "1" : "0", Ms(heldUntil), Now, fence, s_tombstoneLifetime], cancellationToken);

    public Task WithdrawAsync(ushort worldId, long accountId, Guid ticket, CancellationToken cancellationToken) =>
        RunTombstoningAsync(Withdraw, [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)],
            [accountId, ticket.ToString("N"), s_tombstoneLifetime], cancellationToken);

    /// <summary>A script that may write tombstones: an expiry Redis refused (the second result) is logged, throttled.</summary>
    private async Task RunTombstoningAsync(string script, RedisKey[] keys, RedisValue[] values, CancellationToken cancellationToken)
    {
        RedisResult[] r = await RunArrayAsync(script, keys, values, cancellationToken);
        if ((int)r[1] == 1) outage.ReportExpiryRefused();
    }

    public async Task ReleaseHeldAsync(ushort worldId, CancellationToken cancellationToken)
    {
        // The burst of a stop's ends asks it once: inside the window this replica does not ask Redis at all, and the
        // script's own field holds the window across replicas. Recorded only once the script ran: a release that failed
        // or was cancelled leaves the next end to ask again (two at once are harmless, the field guards them).
        long now = Now;
        if (_lastRelease.TryGetValue(worldId, out long last) && now >= last && now - last < s_releaseWindow)
            return;
        await RunAsync(ReleaseHeld, [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)],
            [now, s_releaseWindow], cancellationToken);
        _lastRelease[worldId] = now;
    }

    // The last release this replica asked of each world, Unix ms on the injected clock.
    private readonly ConcurrentDictionary<ushort, long> _lastRelease = new();
    private static readonly long s_releaseWindow = Ms(IWorldPlaceStore.HeldReleaseWindow);

    public Task RejoinAsync(ushort worldId, long accountId, Guid ticket, DateTime until, CancellationToken cancellationToken) =>
        RunAsync(Rejoin, [CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId)],
            [accountId, Ms(until), ticket.ToString("N")], cancellationToken);

    public async Task<QueueCheckIn> CheckInAsync(ushort worldId, long accountId, int? limit, bool available, CancellationToken cancellationToken)
    {
        WorldPlacesConfiguration c = options.Value;
        long now = Now;
        RedisResult[] r = await RunArrayAsync(CheckIn,
            [.. WorldKeys(worldId), CacheKeys.WorldQueueWaiting(accountId), .. OtherWorldKeys(worldId)],
        [
            accountId, now, Limit(limit), available ? "1" : "0", WorldPlacesConfiguration.FrontPositions,
            Ms(c.TimeoutAt(1)), Ms(c.TimeoutAt(WorldPlacesConfiguration.FrontPositions + 1)),
            now + Ms(c.TurnWindow), CacheKeys.WorldQueueWaitingPrefix, (int)worldId, Ms(c.UnclaimedPlace),
            Ms(GameAuthPolicy.WorldFullRetryAfterMin),
        ], cancellationToken);
        if ((int)r[4] == 1) outage.ReportExpiryRefused();
        return new((QueueState)(int)r[0], (int)r[1], new((int)r[2], (int)r[3]));
    }

    public async Task<bool> LeaveAsync(ushort worldId, long accountId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RedisResult result = await cache.Database.ScriptEvaluateAsync(Leave,
                [.. WorldKeys(worldId), CacheKeys.WorldQueueWaiting(accountId)], [accountId, (int)worldId, Now])
            .WaitAsync(cancellationToken);
        return (long)result == 1;
    }

    public async Task<WorldPlaceCounts> CountAsync(ushort worldId, CancellationToken cancellationToken)
    {
        RedisResult[] r = await RunArrayAsync(Count, WorldKeys(worldId),
            [Now, CacheKeys.WorldQueueWaitingPrefix, (int)worldId, Ms(options.Value.UnclaimedPlace), Ms(GameAuthPolicy.WorldFullRetryAfterMin)],
            cancellationToken);
        if ((int)r[5] == 1) outage.ReportExpiryRefused();
        return new((int)r[0], (int)r[1], (int)r[2], new((int)r[3], (int)r[4]));
    }

    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>
    /// How long an ended session's tombstone lasts, in ms, counted by Redis from the moment it is written (<c>HPEXPIRE</c>,
    /// relative, so no skew between identity's clock and Redis's shortens it): one lease, past any renewal that session
    /// still has in flight, since a renewal is answered within its lease.
    /// </summary>
    private static readonly long s_tombstoneLifetime = Ms(GameAuthPolicy.SessionLeaseLifetime);
    private static long Ms(TimeSpan span) => (long)span.TotalMilliseconds;

    /// <summary>A UTC time as Unix milliseconds, as the scripts and the hold marks spell it.</summary>
    public static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    /// <summary>A limit for a script: the number, or the empty string for none, which Lua's <c>tonumber</c> reads as nil.</summary>
    private static RedisValue Limit(int? limit) => limit is { } value ? value : RedisValue.EmptyString;

    /// <summary>The places and marks of every other configured world, one pair per world however many workloads name it.</summary>
    private IEnumerable<RedisKey> OtherWorldKeys(ushort worldId)
    {
        foreach (ushort other in workloads.Value.Servers.Select(s => s.WorldId).Where(id => id != worldId).Distinct())
        {
            yield return CacheKeys.WorldPlaces(other);
            yield return CacheKeys.WorldPlaceMarks(other);
        }
    }

    // RedisValue has implicit conversions from int and long but not from ushort, hence the (int) casts on world ids.
    private static RedisKey[] WorldKeys(ushort worldId) =>
    [
        CacheKeys.WorldPlaces(worldId), CacheKeys.WorldPlaceMarks(worldId), CacheKeys.WorldQueue(worldId),
        CacheKeys.WorldQueueCheckIns(worldId),
    ];

    private async Task RunAsync(string script, RedisKey[] keys, RedisValue[] values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await cache.Database.ScriptEvaluateAsync(script, keys, values).WaitAsync(cancellationToken);
    }

    private async Task<RedisResult[]> RunArrayAsync(string script, RedisKey[] keys, RedisValue[] values, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RedisResult result = await cache.Database.ScriptEvaluateAsync(script, keys, values).WaitAsync(cancellationToken);
        return (RedisResult[])result!;
    }
}

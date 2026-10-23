namespace Avalon.Combat;

/// <summary>
/// A unit's ward (#1178): a pool in front of health that takes what a hit lets it absorb, and refills on its own once
/// the unit has gone <see cref="RegenConfiguration.WardRechargeDelaySeconds" /> without damage, linearly to full over
/// <see cref="RegenConfiguration.WardRechargeSeconds" />. Any damage pauses the recharge, absorbed or not; heals never
/// restore it. Pure and allocation-free: the time is passed in, from the owner's clock.
/// </summary>
/// <remarks>
/// The recharge is a function of the last damage alone, so it does not matter how often <see cref="Advance" /> runs:
/// with <c>t = now - lastDamage - delay ≥ 0</c>, the ward is <c>min(Max, atLastDamage + floor(Max × t / recharge))</c>.
/// A dead unit is not advanced: <see cref="Empty" /> at death, and a revive counts as damage (<see cref="Damaged" />).
/// </remarks>
public struct WardPool
{
    private uint _max;
    private uint _current;
    private uint _atLastDamage;
    private DateTime _lastDamage;

    /// <summary>What a full recharge reaches.</summary>
    public readonly uint Max => _max;

    /// <summary>What the ward holds now.</summary>
    public readonly uint Current => _current;

    /// <summary>Full at <paramref name="max" />: entering the world, a level-up refill, a creature's spawn or reset at home.</summary>
    public void Reset(uint max)
    {
        _max = max;
        _current = max;
        _atLastDamage = max;
    }

    /// <summary>
    /// A new maximum from a stats change: the ward keeps its share (<see cref="CharacterStatsCalculator.KeepShare" />),
    /// and so does what it held at the last damage, so a recharge under way carries on at the new size. A ward that had no
    /// maximum keeps its share of none: it starts empty, and recharges once the delay has passed from
    /// <paramref name="now" />. Only <see cref="Reset" /> fills a ward.
    /// </summary>
    public void SetMax(uint max, DateTime now)
    {
        if (_max == 0)
        {
            _max = max;
            _current = 0;
            Damaged(now);
            return;
        }

        _current = CharacterStatsCalculator.KeepShare(_current, _max, max);
        _atLastDamage = CharacterStatsCalculator.KeepShare(_atLastDamage, _max, max);
        _max = max;
    }

    /// <summary>Takes what it can of <paramref name="amount" />, at most what it holds, pauses the recharge, and returns what it took.</summary>
    public uint Absorb(uint amount, DateTime now)
    {
        uint taken = Math.Min(_current, amount);
        _current -= taken;
        Damaged(now);
        return taken;
    }

    /// <summary>Damage that did not meet the ward (blight, a bleed, a hit it could not hold): the recharge pauses all the same.</summary>
    public void Damaged(DateTime now)
    {
        _lastDamage = now;
        _atLastDamage = _current;
    }

    /// <summary>Death: the ward is empty, and its clock starts from now.</summary>
    public void Empty(DateTime now)
    {
        _current = 0;
        Damaged(now);
    }

    /// <summary>Recharges to what <paramref name="now" /> allows. True when <see cref="Current" /> changed.</summary>
    public bool Advance(DateTime now, RegenConfiguration config)
    {
        if (_current >= _max)
            return false;

        double since = (now - _lastDamage).TotalSeconds - config.WardRechargeDelaySeconds;
        if (!(since >= 0))
            return false;

        uint next = _max;
        if (config.WardRechargeSeconds > 0)
        {
            double gained = Math.Floor(_max * since / config.WardRechargeSeconds);
            if (_atLastDamage + gained < _max)
                next = _atLastDamage + (uint)gained;
        }

        if (next <= _current)
            return false;

        _current = next;
        return true;
    }
}

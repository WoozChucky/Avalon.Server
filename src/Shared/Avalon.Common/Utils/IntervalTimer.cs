namespace Avalon.Common.Utils;

public class IntervalTimer
{
    private long _interval;
    private long _current;

    public IntervalTimer()
    {
        _interval = 0;
        _current = 0;
    }

    public void Update(long diff)
    {
        _current += diff;
        if (_current < 0)
        {
            _current = 0;
        }
    }

    public bool Passed()
    {
        return _current >= _interval;
    }

    public void Reset()
    {
        if (_current >= _interval)
        {
            _current %= _interval;
        }
    }

    public void SetCurrent(long current)
    {
        _current = current;
    }

    public void SetInterval(long interval)
    {
        _interval = interval;
    }

    public long GetInterval()
    {
        return _interval;
    }

    public long GetCurrent()
    {
        return _current;
    }
}

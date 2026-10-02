namespace Avalon.Balance.Service.Runs;

public enum RunState
{
    Queued = 0,
    Running = 1,
    Done = 2,
    Invalid = 3,
    Failed = 4,
    Cancelled = 5,
}

using Avalon.Common;

namespace Avalon.World.Public;

public interface IObject
{
    ObjectGuid Guid { get; set; }

    static uint GenerateId() => UniqueObjectIdGenerator.GenerateId();
}

internal static class UniqueObjectIdGenerator
{
    private static uint s_nextId = 1;
    private static readonly object s_lock = new();

    public static uint GenerateId()
    {
        lock (s_lock)
        {
            return s_nextId++;
        }
    }
}

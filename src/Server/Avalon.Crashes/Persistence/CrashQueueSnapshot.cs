namespace Avalon.Crashes.Persistence;

public sealed record CrashQueueSnapshot(long Pending, DateTimeOffset? OldestReceivedAt,
    DateTimeOffset? LastInventorySweep, long Expired);

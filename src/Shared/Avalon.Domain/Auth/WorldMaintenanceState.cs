namespace Avalon.Domain.Auth;

public sealed record WorldMaintenanceState(bool Enabled, long Revision, DateTime? DeadlineUtc);

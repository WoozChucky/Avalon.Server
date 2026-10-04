using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

public enum GameSessionState { Pending, Active, Ended }

/// <summary>One durable head per account. Ending a session keeps its fence, so later sessions never reuse it.</summary>
public sealed class GameSession
{
    public AccountId AccountId { get; set; } = null!;
    public Guid GameSessionId { get; set; }
    public Guid GameContextId { get; set; }
    public long FencingToken { get; set; }
    public required string ServerId { get; set; }
    public ushort WorldId { get; set; }
    public required string Environment { get; set; }
    public GameSessionState State { get; set; }
    public int CredentialsVersion { get; set; }
    public long SessionEpoch { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LeaseUntil { get; set; }
    public DateTime LicenseUntil { get; set; }
    public Guid? PreviousGameSessionId { get; set; }
    public string? PreviousServerId { get; set; }
    public ushort? PreviousWorldId { get; set; }
}

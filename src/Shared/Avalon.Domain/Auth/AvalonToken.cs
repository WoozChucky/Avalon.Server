using Avalon.Domain.Attributes;

namespace Avalon.Domain.Auth;

public class AvalonToken
{
    [Column("Id")] public Guid Id { get; set; }
    [Column("AccountId")] public int AccountId { get; set; }
    [Column("Hash")] public byte[] Hash { get; set; } = Array.Empty<byte>();
    [Column("Revoked")] public bool Revoked { get; set; }
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("ExpiresAt")] public DateTime ExpiresAt { get; set; }
}

using Avalon.Domain.Attributes;

namespace Avalon.Domain.Auth;

public class RefreshToken
{
    [Column("Id")] public Guid Id { get; set; }
    [Column("AccountId")] public int AccountId { get; set; }
    [Column("Index")] public uint Index { get; set; } = 0;
    [Column("Hash")] public byte[] Hash { get; set; } = Array.Empty<byte>();
    [Column("Revoked")] public bool Revoked { get; set; }
    [Column("Usages")] public uint Usages { get; set; } = 0;
    [Column("CreatedAt")] public DateTime CreatedAt { get; set; }
    [Column("ExpiresAt")] public DateTime ExpiresAt { get; set; }
}

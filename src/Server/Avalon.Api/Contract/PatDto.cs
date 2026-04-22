namespace Avalon.Api.Contract;
public class PatDto
{
    public uint Id { get; set; }
    public long AccountId { get; set; }
    public string Name { get; set; } = "";
    public string Prefix { get; set; } = "";
    public AccountAccessLevel Roles { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

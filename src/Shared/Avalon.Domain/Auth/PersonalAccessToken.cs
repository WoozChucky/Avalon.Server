using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

public class PersonalAccessToken : IDbEntity<PersonalAccessTokenId>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public PersonalAccessTokenId Id { get; set; } = null!;

    [Required]
    public AccountId AccountId { get; set; } = null!;

    [Required, MaxLength(128)]
    public string Name { get; set; } = "";

    [Required]
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();

    [Required, MaxLength(16)]
    public string TokenPrefix { get; set; } = "";

    [Required]
    public AccountAccessLevel Roles { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ExpiresAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public AccountId? RevokedBy { get; set; }
}

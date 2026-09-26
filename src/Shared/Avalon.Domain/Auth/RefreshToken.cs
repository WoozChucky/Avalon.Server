using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Avalon.Common.ValueObjects;

namespace Avalon.Domain.Auth;

public class RefreshToken : IDbEntity<Guid>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Required]
    public Account Account { get; set; }

    public AccountId AccountId { get; set; }

    public Guid FamilyId { get; set; }

    public uint Index { get; set; } = 0;
    public byte[] Hash { get; set; } = [];
    public bool Revoked { get; set; }
    public uint Usages { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The account's <see cref="Account.CredentialsVersion"/> when the family's login proved the
    /// credentials (#495). A token whose version is no longer the account's cannot be rotated.
    /// </summary>
    public int CredentialsVersion { get; set; }
}

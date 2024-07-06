
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Avalon.Domain.Auth;

public class MFASetup : IDbEntity<Guid>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Required]
    public Account Account { get; set; }
    
    public AccountId AccountId { get; set; }

    [Required]
    public byte[] Secret { get; set; }

    [Required]
    public byte[] RecoveryCode1 { get; set; }

    [Required]
    public byte[] RecoveryCode2 { get; set; }

    [Required]
    public byte[] RecoveryCode3 { get; set; }

    [Required]
    public MfaSetupStatus Status { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ConfirmedAt { get; set; }
}

public enum MfaSetupStatus : ushort {
    Confirmed = 0,
    Setup = 1,
    Reset = 2
}

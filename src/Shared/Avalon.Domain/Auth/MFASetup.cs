using Avalon.Domain.Attributes;

namespace Avalon.Domain.Auth;

public class MFASetup {
    [Column("Id")]
    public Guid? Id { get; set; }
    [Column("AccountId")]
    public int AccountId { get; set; }
    [Column("Secret")]
    public byte[] Secret { get; set; }
    [Column("RecoveryCode1")]
    public byte[] RecoveryCode1 { get; set; }
    [Column("RecoveryCode2")]
    public byte[] RecoveryCode2 { get; set; }
    [Column("RecoveryCode3")]
    public byte[] RecoveryCode3 { get; set; }
    [Column("Status")]
    public Status Status { get; set; }
    [Column("CreatedAt")]
    public DateTime CreatedAt { get; set; }
    [Column("ConfirmedAt")]
    public DateTime? ConfirmedAt { get; set; }
}

public enum Status : ushort {
    Confirmed = 0,
    Setup = 1,
    Reset = 2
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Avalon.Domain.Auth;

public class Device : IDbEntity<Guid>
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }
    
    [Required]
    public Account Account { get; set; }
    public AccountId AccountId { get; set; }
    
    public string Name { get; set; } = string.Empty;
    public string Metadata { get; set; } = "{}";
    public bool Trusted { get; set; }
    public DateTime TrustEnd { get; set; }
    public DateTime LastUsage { get; set; }
}

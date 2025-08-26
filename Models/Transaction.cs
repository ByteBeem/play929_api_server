using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{

public class FinancialAudit
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    public string WalletAddress { get; set; }
    
    [StringLength(50)]
    public string Action { get; set; } 
    
    public int WalletId { get; set; }
    
    public decimal Amount { get; set; }
    public decimal PreviousBalance { get; set; }
    public decimal NewBalance { get; set; }
    public DateTime Timestamp { get; set; }
    
    [StringLength(255)]
    public string Reference { get; set; }
    
    
    
    [ForeignKey("WalletAddress")]
    public virtual Wallet Wallet { get; set; }
}

public class Transaction
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public int UserId { get; set; }
    
    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Type { get; set; } 
    
    [StringLength(255)]
    public string Description { get; set; }
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    [Required]
    [StringLength(20)]
    public string Status { get; set; } 
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal BeforeBalance { get; set; }
    
    [Column(TypeName = "decimal(18,2)")]
    public decimal AfterBalance { get; set; }
    
    public Guid? RelatedTransactionId { get; set; }

    public string WalletAddress { get; set; }
    
    public int WalletId { get; set; }

    [ForeignKey(nameof(WalletId))]
    public virtual Wallet Wallet { get; set; }



    [ForeignKey("UserId")]
    public virtual User User { get; set; }
}
}
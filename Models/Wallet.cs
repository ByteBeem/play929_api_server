using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{
    public class Wallet
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }  
        
        [ForeignKey("User")]
        public int UserId { get; set; }  

        
        [Required]
        [Column(TypeName = "decimal(18, 2)")]
        [Range(0, double.MaxValue, ErrorMessage = "Balance cannot be negative.")]
        public decimal Balance { get; set; } = 20.00m;

        [Required]
        [StringLength(100)]
        public string WalletAddress { get; set; }

        [StringLength(50)]
        public string WalletType { get; set; } = "Standard"; 

        [StringLength(3)]
        public string Currency { get; set; } = "ZAR";

        public bool IsFrozen { get; set; } = false; 

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        public virtual User User { get; set; }
    }
}
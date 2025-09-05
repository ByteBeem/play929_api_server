using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{
 
 
 public class RefreshToken
    {
        [Key]
        public Guid Id { get; set; }
        
        [Required]
        public string Token { get; set; }
        
        [Required]
        public DateTime Expires { get; set; }
        
        public bool Revoked { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        [ForeignKey("UserId")]
        public virtual User User { get; set; }
    }
}
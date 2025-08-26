using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(13, MinimumLength = 13, ErrorMessage = "ID Number must be 13 digits.")]
        public string IdNumber { get; set; }

        [Required]
        [StringLength(100)]
        public string FullNames { get; set; }

        [Required]
        [StringLength(100)]
        public string Surname { get; set; }

        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; }

        [Required]
        [Phone]
        [StringLength(20)]
        public string PhoneNumber { get; set; }

        [Required]
        [StringLength(100)]
        public string PasswordHash { get; set; }

        [Required]
        [StringLength(20)]
        public string AccountNumber { get; set; }

        [Required]
        [StringLength(20)]
        public string Role { get; set; } = "User"; 
        
        public bool IsEmailVerified { get; set; } = false;
        public bool IsActive { get; set; } = false;

        public string SecurityStamp { get; set; }



        public DateTime? LastPasswordChange { get; set; }
        
        public bool IsLocked { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLogin { get; set; }

        // Navigation Properties
        public virtual Wallet Wallet { get; set; }

        
    }
}

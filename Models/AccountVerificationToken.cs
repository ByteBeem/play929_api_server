using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace Play929Backend.Models
{

public class AccountVerificationToken
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Token { get; set; }

    [Required]
    public int UserId { get; set; } 

    [ForeignKey("UserId")]
    public virtual User User { get; set; } 

    public bool Used { get; set; } = false;

    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24); 

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
}

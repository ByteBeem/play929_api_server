using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{

public class SecurityLog
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Action { get; set; } 
    public string IPAddress { get; set; }
    public string Description { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


    [ForeignKey("UserId")]
    public virtual User User { get; set; }
}
}
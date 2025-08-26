using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{

public class Notification
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Message { get; set; }
    public string Type { get; set; } 

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }


    [ForeignKey("UserId")]
    public virtual User User { get; set; }
}
}

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{

public class AuditTrail
{
    [Key]
    public int Id { get; set; }

    public string Action { get; set; }
    public string PerformedBy { get; set; } 
    public string TargetEntity { get; set; }
    public string Details { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
}
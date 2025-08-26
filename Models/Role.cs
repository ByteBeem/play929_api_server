using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{

public class Role
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string Name { get; set; } 
}

public class UserRole
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }
    public int RoleId { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; }

    [ForeignKey("RoleId")]
    public virtual Role Role { get; set; }
}
}
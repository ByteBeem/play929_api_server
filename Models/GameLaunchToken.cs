using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{
    public class GameLaunchToken
    {
        public Guid Id { get; set; }

        [Required]
        public string LaunchToken { get; set; } 

        public DateTime ExpiresAt { get; set; }   

        public bool Used { get; set; } = false;


        public Guid GameSessionId { get; set; }

        [ForeignKey("GameSessionId")]
        public virtual GameSession GameSession { get; set; }
    }
}

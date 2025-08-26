using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Play929Backend.Models
{
    public class GameSession
    {
        public Guid Id { get; set; }

        [Required]
        public string GameName { get; set; }

        [Required]
        public string SessionToken { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public bool IsActive { get; set; } = true;

        
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User User { get; set; }

        
        public virtual ICollection<GameLaunchToken> LaunchTokens { get; set; } = new List<GameLaunchToken>();
    }
}

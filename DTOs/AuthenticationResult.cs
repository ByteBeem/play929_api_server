    using Play929Backend.Models;
    

    namespace Play929Backend.DTOs
{

    public class AuthenticationResult
    {
        public bool IsSuccess { get; set; }
        public User? User { get; set; }
        public string? FailureReason { get; set; }
        public bool IsLocked { get; set; }
        public bool IsPasswordExpired { get; set; }

        public static AuthenticationResult Success(User user) => new() 
        { 
            IsSuccess = true, 
            User = user 
        };

        public static AuthenticationResult Failed(string reason) => new() 
        { 
            IsSuccess = false, 
            FailureReason = reason 
        };

        public static AuthenticationResult Locked(string reason) => new() 
        { 
            IsSuccess = false, 
            IsLocked = true, 
            FailureReason = reason 
        };

        public static AuthenticationResult PasswordExpired(string reason) => new() 
        { 
            IsSuccess = false, 
            IsPasswordExpired = true, 
            FailureReason = reason 
        };
    }
}
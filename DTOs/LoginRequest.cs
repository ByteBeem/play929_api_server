     using Play929Backend.Models;
     using System.ComponentModel.DataAnnotations;

    

    namespace Play929Backend.DTOs
{

 
 public class LoginRequest
    {
        [Required(ErrorMessage = "Email or phone is required")]
        [StringLength(255, MinimumLength = 3)]
        public string Email { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
    }

}

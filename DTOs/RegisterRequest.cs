    using Play929Backend.Models;
    using System.ComponentModel.DataAnnotations;

    

    namespace Play929Backend.DTOs
{

        public class RegisterRequest
        {
            [Required, StringLength(100, MinimumLength = 2)]
            public string FullNames { get; set; }

            [Required, StringLength(50, MinimumLength = 2)]
            public string Surname { get; set; }

            [Required, EmailAddress, StringLength(255)]
            public string Email { get; set; }

            [Required, Phone, StringLength(15)]
            public string PhoneNumber { get; set; }

            [Required, StringLength(13, MinimumLength = 13)]
            public string IdNumber { get; set; }

            [Required, DataType(DataType.Password)]
            public string Password { get; set; }

            [DataType(DataType.Password), Compare(nameof(Password))]
            public string ConfirmPassword { get; set; }
        }
}
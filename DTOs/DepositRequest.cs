using System.ComponentModel.DataAnnotations;

namespace Play929Backend.DTOs
{
    public class DepositRequest
    {
        [Required(ErrorMessage = "Account number is required")]
        public string AccountNumber { get; set; }

        [Required(ErrorMessage = "Payment type is required")]
        [StringLength(100, ErrorMessage = "Payment type is too long")]
        public string Type { get; set; }


        [Required(ErrorMessage = "Amount is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal Amount { get; set; }
    }
}

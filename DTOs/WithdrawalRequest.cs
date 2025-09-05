using System.ComponentModel.DataAnnotations;

namespace Play929Backend.DTOs
{
    public class WithdrawalRequest
    {
        [Required(ErrorMessage = "Wallet address is required")]
        public string WalletAddress { get; set; }

        [Required(ErrorMessage = "Bank name is required")]
        [StringLength(100, ErrorMessage = "Bank name is too long")]
        public string Bank { get; set; }

        [Required(ErrorMessage = "Bank account number is required")]
        [StringLength(50, ErrorMessage = "Bank account number is too long")]
        public string BankAccount { get; set; }

        [Required(ErrorMessage = "Amount is required")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal Amount { get; set; }
    }
}

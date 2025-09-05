       namespace Play929Backend.DTOs
{

   
   public class TransactionResult
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public decimal? NewBalance { get; set; }
        public string Message { get; set; } = string.Empty;

        public static TransactionResult Success(decimal newBalance) => new() 
        { 
            IsSuccess = true, 
            NewBalance = newBalance 
        };

        public static TransactionResult Failed(string message) => new() 
        { 
            IsSuccess = false, 
             Message = message
        };
    }
}
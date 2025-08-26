using Play929Backend.Models;
using Play929Backend.DTOs;

namespace Play929Backend.Services.Interfaces
{
    public interface IWalletService
    {
         Task<string> GenerateWalletAddressAsync(string accountNumber);
         Task<decimal> GetBalanceAsync(string walletAddress);
         Task<TransactionResult> DepositAsync(string walletAddress, decimal amount, string reference);
         Task<TransactionResult> WithdrawAsync(string walletAddress, decimal amount, string reference);
         Task<Wallet?> GetWalletByEmailAsync(string email);
         Task<List<Transaction>> GetTransactionsAsync(Wallet wallet);
     
    }
}

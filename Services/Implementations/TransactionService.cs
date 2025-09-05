using System.Threading.Tasks;
using Play929Backend.Data;
using Play929Backend.Models;
using Play929Backend.Services.Interfaces;



namespace Play929Backend.Services.Implementations
{
public class TransactionService : ITransactionService
{
    private readonly AppDbContext _context;

    public TransactionService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Transaction> CreateAsync(Transaction transaction)
    {
        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();
        return transaction;
    }
}
}

using Play929Backend.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Play929Backend.Services.Interfaces
{
    public interface ITransactionService
    {
        Task<Transaction> CreateAsync(Transaction transaction); 

    }
}

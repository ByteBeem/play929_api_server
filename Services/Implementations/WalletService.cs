using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Play929Backend.Models;
using Play929Backend.Services.Interfaces;
using Play929Backend.Data;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Play929Backend.DTOs;
using Microsoft.Extensions.Configuration;

namespace Play929Backend.Services.Implementations
{
    public class WalletService : IWalletService
    {
        private const string BalanceLockPrefix = "bal_lock_";
        private const int AddressMaxRetries = 5;
        private const decimal MaxDailyTransactionAmount = 50000m;

        private readonly AppDbContext _context;
        private readonly ILogger<WalletService> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly ISecurityLogService _securityLogService;
        private readonly IAuditService _auditService;
        private readonly IConfiguration _config;

        public WalletService(
            AppDbContext context,
            ILogger<WalletService> logger,
            IMemoryCache memoryCache,
            ISecurityLogService securityLogService,
            IAuditService auditService,
            IConfiguration config)
        {
            _context = context;
            _logger = logger;
            _memoryCache = memoryCache;
            _securityLogService = securityLogService;
            _auditService = auditService;
            _config = config;
        }

        public async Task<string> GenerateWalletAddressAsync(string accountNumber)
        {
            for (int i = 0; i < AddressMaxRetries; i++)
            {
                var address = GenerateCryptoAddress();
                if (!await _context.Wallets.AnyAsync(w => w.WalletAddress == address))
                    return address;

                await Task.Delay((int)Math.Pow(2, i) * 100);
            }
            throw new InvalidOperationException("Failed to generate unique wallet address");
        }

        public async Task<decimal> GetBalanceAsync(string walletAddress)
        {
            var wallet = await _context.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.WalletAddress == walletAddress);

            if (wallet == null)
                throw new ArgumentException("Invalid wallet address");

            return wallet.Balance;
        }

         public async Task<Wallet?> GetWalletByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email cannot be null or empty.", nameof(email));

            var wallet = await _context.Wallets
                .Include(w => w.User) 
                .FirstOrDefaultAsync(w => w.User.Email == email);

            return wallet;
        }

        public async Task<List<Transaction>> GetTransactionsAsync(Wallet wallet)
        {
            var transactions = await _context.Transactions
            .AsNoTracking()
            .Where(t => t.WalletAddress == wallet.WalletAddress)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();

            return transactions;
        }

        public async Task<TransactionResult> DepositAsync(string walletAddress, decimal amount, string reference)
        {
            ValidateTransactionAmount(amount);

            var lockKey = BalanceLockPrefix + walletAddress;
            using (await LockProvider.AcquireLockAsync(lockKey))
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.WalletAddress == walletAddress);
                    if (wallet == null) return TransactionResult.Failed("Invalid wallet");
                    if (wallet.IsFrozen) return TransactionResult.Failed("Wallet frozen");

                    var dailyDeposits = await _context.Transactions
                        .Where(t => t.WalletId == wallet.Id && t.Type == "Deposit" && t.Timestamp >= DateTime.UtcNow.AddDays(-1))
                        .SumAsync(t => t.Amount);

                    if (dailyDeposits + amount > MaxDailyTransactionAmount)
                        return TransactionResult.Failed("Daily deposit limit exceeded");

                    var before = wallet.Balance;
                    wallet.Balance += amount;
                    wallet.UpdatedAt = DateTime.UtcNow;

                    var tx = new Transaction
                    {
                        WalletId = wallet.Id,
                        Amount = amount,
                        Type = "Deposit",
                        Description = reference,
                        Timestamp = DateTime.UtcNow,
                        Status = "Completed",
                        BeforeBalance = before,
                        AfterBalance = wallet.Balance
                    };

                    _context.Transactions.Add(tx);
                    await _context.SaveChangesAsync();

                    await _auditService.LogFinancialEventAsync(new FinancialAudit
                    {
                        WalletAddress = wallet.WalletAddress,
                        Action = "Deposit",
                        Amount = amount,
                        PreviousBalance = before,
                        NewBalance = wallet.Balance,
                        Timestamp = DateTime.UtcNow,
                        Reference = reference
                    });

                    await transaction.CommitAsync();
                    return TransactionResult.Success(wallet.Balance);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Deposit failed for wallet {WalletAddress}", walletAddress);
                    return TransactionResult.Failed("Transaction failed");
                }
            }
        }

        public async Task<TransactionResult> WithdrawAsync(string walletAddress, decimal amount, string reference)
        {
            ValidateTransactionAmount(amount);

            var lockKey = BalanceLockPrefix + walletAddress;
             using (await LockProvider.AcquireLockAsync(lockKey))
            {
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.WalletAddress == walletAddress);
                    if (wallet == null) return TransactionResult.Failed("Invalid wallet");
                    if (wallet.IsFrozen) return TransactionResult.Failed("Wallet frozen");
                    if (wallet.Balance < amount) return TransactionResult.Failed("Insufficient funds");

                    var before = wallet.Balance;
                    wallet.Balance -= amount;
                    wallet.UpdatedAt = DateTime.UtcNow;

                    var tx = new Transaction
                    {
                        WalletAddress = wallet.WalletAddress,
                        Amount = amount,
                        Type = "Withdraw",
                        Description = reference,
                        Timestamp = DateTime.UtcNow,
                        Status = "Completed",
                        BeforeBalance = before,
                        AfterBalance = wallet.Balance
                    };

                    _context.Transactions.Add(tx);
                    await _context.SaveChangesAsync();

                    await _auditService.LogFinancialEventAsync(new FinancialAudit
                    {
                        WalletAddress = wallet.WalletAddress,
                        Action = "Withdraw",
                        Amount = amount,
                        PreviousBalance = before,
                        NewBalance = wallet.Balance,
                        Timestamp = DateTime.UtcNow,
                        Reference = reference
                    });

                    await transaction.CommitAsync();
                    return TransactionResult.Success(wallet.Balance);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Withdraw failed for wallet {WalletAddress}", walletAddress);
                    return TransactionResult.Failed("Transaction failed");
                }
            }
        }

        private string GenerateCryptoAddress()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[20];
            rng.GetBytes(bytes);
            return "W" + BitConverter.ToString(bytes).Replace("-", "").Substring(0, 34);
        }

        private void ValidateTransactionAmount(decimal amount)
        {
            if (amount <= 0)
                throw new ArgumentException("Amount must be positive");

            if (decimal.Round(amount, 2) != amount)
                throw new ArgumentException("Amount must have max 2 decimal places");

            var maxSingleTx = _config.GetValue<decimal>("Wallet:MaxSingleTransaction", 10000m);
            if (amount > maxSingleTx)
                throw new InvalidOperationException($"Amount exceeds single transaction limit of {maxSingleTx}");
        }
    }

    public static class LockProvider
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public static async Task<IDisposable> AcquireLockAsync(string key, int timeoutMs = 5000)
        {
            var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (!await semaphore.WaitAsync(timeoutMs))
                throw new TimeoutException($"Failed to acquire lock for {key}");

            return new LockReleaser(semaphore);
        }

        private class LockReleaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            public LockReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
            public void Dispose() => _semaphore.Release();
        }
    }
}

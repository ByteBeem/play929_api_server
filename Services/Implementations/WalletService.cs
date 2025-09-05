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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

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

       public async Task<TransactionResult> DepositPayfast(
    string walletAddress,
    decimal amount,
    string description,
    string idempotencyKey,
    Wallet wallet)
{
    if (string.IsNullOrWhiteSpace(idempotencyKey))
        return TransactionResult.Failed("Missing idempotency key");

    ValidateTransactionAmount(amount);

    var lockKey = BalanceLockPrefix + walletAddress;
    using (await LockProvider.AcquireLockAsync(lockKey))
    {
        Transaction tx;
        try
        {
            // 1️⃣ Check idempotency cache
            if (_memoryCache.TryGetValue(idempotencyKey, out TransactionResult cachedResult))
                return cachedResult;

            // 2️⃣ Check if transaction already exists in DB
            var existingTx = await _context.Transactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey);

            if (existingTx != null)
            {
                var result = TransactionResult.Success(existingTx.AfterBalance);
                _memoryCache.Set(idempotencyKey, result, TimeSpan.FromMinutes(10));
                return result;
            }

            // 3️⃣ Validate wallet status
            if (wallet.IsFrozen)
                return TransactionResult.Failed("Wallet frozen");

            var dailyDeposits = await _context.Transactions
                .Where(t => t.WalletId == wallet.Id &&
                            t.Type == "Deposit" &&
                            t.Timestamp >= DateTime.UtcNow.AddDays(-1))
                .SumAsync(t => t.Amount);

            if (dailyDeposits + amount > MaxDailyTransactionAmount)
                return TransactionResult.Failed("Daily deposit limit exceeded");

            // 4️⃣ Create pending transaction in DB
            var before = wallet.Balance;
            var after = wallet.Balance + amount; // tentative balance

            tx = new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = wallet.UserId,
                WalletId = wallet.Id,
                WalletAddress = wallet.WalletAddress,
                Amount = amount,
                Type = "Deposit",
                Description = description,
                Timestamp = DateTime.UtcNow,
                Status = "Pending",
                BeforeBalance = before,
                AfterBalance = after,
                IdempotencyKey = idempotencyKey
            };

            _context.Transactions.Add(tx);

            // Note: We don't update wallet.Balance yet; will update on confirmation
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deposit failed during DB save for wallet {WalletAddress}", walletAddress);
            return TransactionResult.Failed("Transaction failed");
        }

        // 5️⃣ Call external payment API outside transaction
        PayfastResponse request;
        try
        {
            request = await CreatePaymentAsync(
                amount: amount,
                itemName: "Wallet Deposit",
                returnUrl: "https://yourapp.com/deposit/success",
                cancelUrl: "https://yourapp.com/deposit/cancel",
                notifyUrl: "https://yourapp.com/api/payfast/notify",
                email: wallet.User.Email);

            Console.WriteLine($"Payfast response: {request.Status}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deposit failed calling Payfast for wallet {WalletAddress}", walletAddress);
            // Optionally, mark transaction as Failed here or leave Pending for manual review
            return TransactionResult.Failed("Payment gateway error");
        }

        // 6️⃣ Cache the pending transaction result
        var resultToReturn = TransactionResult.Success(tx.AfterBalance); // still pending
        _memoryCache.Set(idempotencyKey, resultToReturn, TimeSpan.FromMinutes(10));

        return resultToReturn;
    }
}


        public async Task<Wallet?> GetWalletByAccountNumber(string accountNumber)
        {
            if (string.IsNullOrWhiteSpace(accountNumber))
                throw new ArgumentException("Account number cannot be null or empty.", nameof(accountNumber));

            var wallet = await _context.Wallets
                .AsNoTracking()
                .Include(w => w.User) 
                .FirstOrDefaultAsync(w => w.User.AccountNumber == accountNumber);

            return wallet; 
        }



          private async Task<PayfastResponse> CreatePaymentAsync(decimal amount, string itemName, string returnUrl, string cancelUrl, string notifyUrl, string email)
        {
            var timestamp = GetIsoTimestamp();

            var payload = new Dictionary<string, string>
            {
                { "merchant_id", _config["Payfast:Merchant_ID"] },
                { "merchant_key",_config["Payfast:Merchant_Key"] },
                { "amount", amount.ToString("0.00") },
                { "item_name", itemName },
                { "return_url", returnUrl },
                { "cancel_url", cancelUrl },
                { "notify_url", notifyUrl },
                { "email_address", email },
                { "timestamp", timestamp }
            };

            string passphrase = _config["Payfast:Salt_Passphrase"];
            string signature = GenerateApiSignature(payload, passphrase);

            Console.WriteLine($"Signature: {signature}");

            // Generate signature
            // var signature = GenerateSignature(payload, _config["Payfast:Salt_Passphrase"]);
            payload.Add("signature", signature);

            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(payload);
            var response = await client.PostAsync($"{_config["Payfast:Sandbox_URL"]}/payments", content);
            var responseText = await response.Content.ReadAsStringAsync();
            Console.WriteLine("Raw Payfast response: " + responseText);

           if (responseText.StartsWith("{"))
            {
                return System.Text.Json.JsonSerializer.Deserialize<PayfastResponse>(responseText);
            }
            else
            {
                // Log warning
                _logger.LogWarning("Payfast response is not JSON. Returning raw string.");
                return new PayfastResponse
                {
                    Status = "Unknown",
                    Data = responseText
                };
}
        }


         public static string GenerateApiSignature(Dictionary<string, string> data, string passphrase = "")
    {
        // Add passphrase if provided
        if (!string.IsNullOrEmpty(passphrase))
        {
            data["passphrase"] = passphrase;
        }

        // Sort keys alphabetically
        var sortedKeys = data.Keys.OrderBy(k => k).ToList();

        // Build payload
        var payloadBuilder = new StringBuilder();
        foreach (var key in sortedKeys)
        {
            var value = data[key].Replace("+", " ");
            payloadBuilder.Append($"{key}={WebUtility.UrlEncode(value)}&");
        }

        // Remove trailing &
        if (payloadBuilder.Length > 0)
            payloadBuilder.Length--;  

        // Compute MD5 hash
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(payloadBuilder.ToString()));

        // Convert to lowercase hex string
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }



        private string GenerateCryptoAddress()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[20];
            rng.GetBytes(bytes);
            return "W" + BitConverter.ToString(bytes).Replace("-", "").Substring(0, 34);
        }

        private string GetIsoTimestamp()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"); 
        }

        private string GenerateSignature(Dictionary<string, string> data, string passphrase)
        {
            // Sort data alphabetically
            var sorted = data.OrderBy(kv => kv.Key);
            var query = string.Join("&", sorted.Select(kv => $"{kv.Key}={kv.Value}"));

            if (!string.IsNullOrEmpty(passphrase))
                query += $"&passphrase={passphrase}";

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(query));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
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

    public class PayfastSettings
    {
        public string Merchant_ID { get; set; }
        public string Merchant_Key { get; set; }
        public string Sandbox_URL { get; set; }
        public string Salt_Passphrase { get; set; }
    }

    public class PayfastRequest
    {
        public decimal Amount { get; set; }
        public string ItemName { get; set; }
        public string ReturnUrl { get; set; }
        public string CancelUrl { get; set; }
        public string NotifyUrl { get; set; }
        public string CustomerEmail { get; set; }
        public string Timestamp { get; set; }
        public string Signature { get; set; }
    }

    public class PayfastResponse
    {
        public int Code { get; set; }
        public string Status { get; set; }
        public object Data { get; set; }
    }


}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Play929Backend.Services.Interfaces;
using Play929Backend.Models;
using Play929Backend.DTOs;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using System.Threading.Tasks;
using System;
using System.Text.RegularExpressions;

namespace Play929Backend.Controllers
{
    [ApiController]
    [Route("api/wallet")]
    //[EnableRateLimiting("5PerMinute")]
    public class WalletController : ControllerBase
    {
        private readonly IWalletService _walletService;
        private readonly ISecurityLogService _securityLogService;
        private readonly ILogger<WalletController> _logger;
        private readonly string _jsonFilePath = Path.Combine("PaymentData", "withdrawalMethods.json");
        private readonly string _jsonFileDepositPath = Path.Combine("PaymentData", "depositMethods.json");

        public WalletController(IWalletService walletService, ILogger<WalletController> logger, ISecurityLogService securityLogService)
        {
            _walletService = walletService;
            _logger = logger;
            _securityLogService = securityLogService;
        }

        [Authorize] 
        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized("Email claim missing in token");

            try
            {
                
                var wallet = await _walletService.GetWalletByEmailAsync(email);
                if (wallet == null)
                    return NotFound("Wallet not found");

                if (wallet.IsFrozen)
                    return BadRequest(new{error = "Wallet is frozen contact admin at support@play929.com" });

                if (string.IsNullOrWhiteSpace(wallet.WalletAddress))
                    return BadRequest(new{error = "Wallet address is not set"});


                var balance = await _walletService.GetBalanceAsync(wallet.WalletAddress);
                var userBalance = $"{balance:0.00}";

                if (balance < 0)
                    return BadRequest("Invalid balance retrieved");

                return Ok(new { balance });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Balance retrieval failed");
                return StatusCode(500, new { error = "Failed to retrieve balance" });
            }
        }


         [Authorize] 
        [HttpGet("transactions")]
        public async Task<IActionResult> GetTransactions()
        {
            
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized("Email claim missing in token");

            try
            {
                
                var wallet = await _walletService.GetWalletByEmailAsync(email);
                if (wallet == null)
                    return NotFound("Wallet not found");

                if (wallet.IsFrozen)
                    return BadRequest(new{error = "Wallet is frozen contact admin at support@play929.com" });

                if (string.IsNullOrWhiteSpace(wallet.WalletAddress))
                    return BadRequest(new{error = "Wallet address is not set"});


                var transactions = await _walletService.GetTransactionsAsync(wallet);
                

                return Ok(new { transactions });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "transactions retrieval failed");
                return StatusCode(500, new { error = "Failed to retrieve transactions" });
            }
        }

        [Authorize(Roles = "User")]
        [HttpPost("deposit")]
        [RequestSizeLimit(1_000)] 
        public async Task<IActionResult> Deposit([FromBody] DepositRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            request.AccountNumber = request.AccountNumber?.Trim();

            if (string.IsNullOrWhiteSpace(request.AccountNumber) ||
                string.IsNullOrWhiteSpace(request.Type) ||
                request.Amount <= 0 ) 
            {
                return BadRequest(new { error = "Invalid input" });
            }

            if(request.Amount < 10)
                return BadRequest(new { error = "Minimum deposit is R10" });

            if(request.Amount > 5000)
                return BadRequest(new { error = "Maximum deposit is R5000" });

            var validType = CheckType(request.Type);
            if (validType == "Unknown")
                return BadRequest(new { error = "Invalid payment type" });

            try
            {
                var wallet = await _walletService.GetWalletByAccountNumber(request.AccountNumber);
                if (wallet == null)
                    return NotFound(new { error = "Wallet not found" });

                TransactionResult result;

                switch (validType)
                {
                    case "Payfast":
                        if (!IsValidAccountNumber(request.AccountNumber))
                            return BadRequest(new { error = "Invalid account number" });

                        var description = $"type: {validType}, Account: {request.AccountNumber}, Amount: {request.Amount}";

                        var idempotencyKey =  "123456789000000553344";
                        // HttpContext.Request.Headers["Idempotency-Key"].FirstOrDefault() ||

                        if(string.IsNullOrWhiteSpace(idempotencyKey))
                            return BadRequest(new { error = "Idempotency-Key header is required , Please refresh the page." });

                        result = await _walletService.DepositPayfast(wallet.WalletAddress, request.Amount, description, idempotencyKey , wallet);
                        if (!result.IsSuccess)
                            return BadRequest(new { error = result.Message });
                        break;

                    default:
                        return BadRequest(new { error = "Unsupported payment type" });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deposit failed");
                return StatusCode(500, new { error = "Deposit failed" });
            }
        }


        [Authorize] 
        [HttpPost("withdraw")]
        public async Task<IActionResult> Withdraw([FromBody] WithdrawalRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (string.IsNullOrWhiteSpace(request.Bank) ||
                string.IsNullOrWhiteSpace(request.BankAccount) ||
                request.Amount <= 0)
                return BadRequest("All fields are required");

            try
            {
                var reference = $"Bank: {request.Bank}, Account: {request.BankAccount}, Amount: {request.Amount}";
                var result = await _walletService.WithdrawAsync(request.WalletAddress, request.Amount, reference);
                if (!result.IsSuccess)
                    return BadRequest(new { error = result.Message });

                return Ok(new { balance = result.NewBalance });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Withdrawal failed");
                return StatusCode(500, new { error = "Withdrawal failed" });
            }
        }

            [HttpGet("withdrawalMethods")]
            public async Task<IActionResult> GetWithdrawalMethods()
            {
                if (!System.IO.File.Exists(_jsonFilePath))
                    return NotFound(new { message = "Payment methods file not found." });

                string jsonContent = await System.IO.File.ReadAllTextAsync(_jsonFilePath);

               
                var paymentMethods = JsonSerializer.Deserialize<object>(jsonContent);

                return Ok(paymentMethods);
            }

             [HttpGet("depositMethods")]
            public async Task<IActionResult> GetDepositMethods()
            {
                if (!System.IO.File.Exists(_jsonFileDepositPath))
                    return NotFound(new { message = "Payment methods file not found." });

                string jsonContent = await System.IO.File.ReadAllTextAsync(_jsonFileDepositPath);

               
                var paymentMethods = JsonSerializer.Deserialize<object>(jsonContent);

                return Ok(paymentMethods);
            }


            private static readonly Dictionary<string, string> TypeMappings = new(StringComparer.OrdinalIgnoreCase)
            {
                { "internet banking", "Payfast" },
                { "onemoney", "OneMoney" },
                { "telecash", "TeleCash" },
                { "telecash plus", "TeleCash Plus" },
                { "zipit", "Zipit" }
            };

            private string CheckType(string type)
            {
                if (string.IsNullOrWhiteSpace(type))
                    return "Unknown";

                return TypeMappings.TryGetValue(type.Trim(), out var mapped) ? mapped : "Unknown";
            }

           

        private bool IsValidAccountNumber(string accountNumber)
        {
            if (string.IsNullOrWhiteSpace(accountNumber))
                return false;

            // Regex: starts with PLY- followed by exactly 4 digits
            var pattern = @"^PLY-\d{4}$";
            return Regex.IsMatch(accountNumber, pattern);
        }


    }
}
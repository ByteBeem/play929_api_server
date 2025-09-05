using Microsoft.AspNetCore.Mvc;
using Play929Backend.Models;
using Play929Backend.Services.Interfaces;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Play929Backend.DTOs;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using Play929Backend.Data;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;


namespace Play929Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
   //[EnableRateLimiting("5PerMinute")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IWalletService _walletService;
        private readonly IEmailService _emailService;
        private readonly ITransactionService _transactionService;
        private readonly INotificationService _notificationService;
        private readonly IBackgroundTaskQueue _backgroundQueue;
        private readonly ILogger<UserController> _logger;
        private readonly IConfiguration _config;
        private readonly ISecurityLogService _securityLogService;
        private readonly AppDbContext _context;
        private const string RedirectLink = "https://dashboard.play929.com";

        public UserController(
         AppDbContext context,
            IUserService userService,
            IEmailService emailService,
            IWalletService walletService,
            ITransactionService transactionService,
            INotificationService notificationService,
            IBackgroundTaskQueue backgroundQueue,
            ILogger<UserController> logger,
            IConfiguration config,
            ISecurityLogService securityLogService)
        {
            _userService = userService;
            _emailService = emailService;
            _walletService = walletService;
            _transactionService = transactionService;
            _notificationService = notificationService;
            _backgroundQueue = backgroundQueue;
            _logger = logger;
            _config = config;
            _context = context;
            _securityLogService = securityLogService;
        }

        [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        request.Email = request.Email?.Trim().ToLowerInvariant();
        request.FullNames = request.FullNames?.Trim();
        request.Surname = request.Surname?.Trim();
        request.PhoneNumber = request.PhoneNumber?.Trim();
        request.IdNumber = request.IdNumber?.Trim();

        if (!new EmailAddressAttribute().IsValid(request.Email))
            return BadRequest(new { Error = "Invalid email format" });

        if (string.IsNullOrWhiteSpace(request.IdNumber) || request.IdNumber.Length != 13 || !Regex.IsMatch(request.IdNumber, @"^\d{13}$"))
            return BadRequest(new { Error = "ID Number must be exactly 13 numeric digits" });

        var passwordError = ValidatePassword(request.Password);
        if (passwordError != null)
            return BadRequest(new { Error = passwordError });

        if (request.Password.ToLowerInvariant() != request.ConfirmPassword.ToLowerInvariant())
            return BadRequest(new { Error = "Passwords do not match." });

        if (await _userService.EmailExistsAsync(request.Email))
            return Conflict(new { Error = "Email already registered" });

        if (await _userService.IdNumberExistsAsync(request.IdNumber))
            return Conflict(new { Error = "ID Number already registered" });

        await using var transaction = await _userService.BeginTransactionAsync();

        try
        {
            var accountNumber = await _userService.GenerateAccountNumberAsync();
            var walletAddress = await _walletService.GenerateWalletAddressAsync(accountNumber);
            var bonusAmount = _config.GetValue<decimal>("SignupBonus:Amount", 20.00m);

            var user = new User
            {
                FullNames = request.FullNames,
                Surname = request.Surname,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                IdNumber = request.IdNumber,
                AccountNumber = accountNumber,
                PasswordHash = _userService.HashPassword(request.Password),
                IsEmailVerified = false, 
                IsActive = false,
                LastPasswordChange = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Wallet = new Wallet
                {
                    Balance = bonusAmount,
                    WalletAddress = walletAddress,
                    WalletType = "Standard",
                    Currency = "ZAR",
                    IsFrozen = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            };

            var createdUser = await _userService.RegisterAsync(user, transaction);
            await _userService.AssignRoleAsync(createdUser.Id.ToString(), "User");

            
            var verificationToken = GenerateSecureToken();
            await _userService.SaveEmailVerificationTokenAsync(createdUser.Id, verificationToken);

            // Send templated email
            var verifyLink = $"https://secure.play929.com/verify-email?token={verificationToken}";
            await _emailService.SendTemplateEmailAsync(
                toEmail: createdUser.Email,
                template: EmailTemplate.EmailVerify,
                templateData: new { FullName = createdUser.FullNames, VerifyLink = verifyLink, ExpiryHours = 24 },
                subject: "Verify Your Play929 Account"
            );

            await _securityLogService.LogSecurityEventAsync(new SecurityLog
            {
                UserId = createdUser.Id,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP",
                Action = "Register",
                Description = "User registration completed",
                Timestamp = DateTime.UtcNow
            });

            await _transactionService.CreateAsync(new Transaction
            {
                UserId = createdUser.Id,
                WalletId = createdUser.Wallet.Id,
                Amount = bonusAmount,
                Type = "Bonus",
                Description = "Sign up Bonus",
                Timestamp = DateTime.UtcNow,
                WalletAddress = walletAddress,
                Status = "Completed"
            });

            await _notificationService.CreateAsync(new Notification
            {
                UserId = createdUser.Id,
                Message = "Welcome to Play929! Your account has been created successfully.",
                Type = "Email",
                IsRead = false,
                SentAt = DateTime.UtcNow
            });

            await transaction.CommitAsync();

            return Ok(new
            {
                createdUser.Id,
                createdUser.Email,
                createdUser.AccountNumber,
                Message = "Registration successful. Please verify your email."
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error during registration for {Email}", request.Email);
            return StatusCode(500, new { Error = "Registration failed. Try again later." });
        }
    }

        [HttpPost("login")]
        [EnableRateLimiting("5PerMinute")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(new { Error = "Invalid input" });

            request.Email = request.Email?.Trim().ToLowerInvariant();

            if (!new EmailAddressAttribute().IsValid(request.Email))
                return BadRequest(new { Error = "Invalid email format" });

            if (!IsPasswordStructurallyValid(request.Password))
                return Unauthorized(new { Error = "Incorrect email or password" });

            var result = await _userService.AuthenticateAsync(request.Email, request.Password);

            if (result.IsSuccess)
            {
                var user = result.User!;

                await _notificationService.CreateAsync(new Notification
                {
                    UserId = user.Id,
                    Message = "Someone logged into your account. If this wasn't you, change your password.",
                    Type = "Email",
                    SentAt = DateTime.UtcNow
                });

                return await HandleSuccessfulLogin(user);
            }
            else if (result.IsLocked)
            {
                return Unauthorized(new { Error = "Account locked", Reason = result.FailureReason });
            }
            else if (result.IsPasswordExpired)
            {
                return Unauthorized(new { Error = "Password expired", Reason = result.FailureReason });
            }
            else
            {
                return Unauthorized(new { Error = result.FailureReason ?? "Authentication failed" });
            }
        }

        [HttpPost("refreshToken")]
        public async Task<IActionResult> RefreshToken()
        {
            var refreshToken = Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
                return BadRequest(new { Error = "Refresh token is missing" });

            try
            {
                // Validate refresh token
                var tokenRecord = await _userService.GetRefreshTokenAsync(refreshToken);
                if (tokenRecord == null)
                {
                    Console.WriteLine("Refresh token record not found.");
                    return Unauthorized(new { Error = "Invalid or revoked refresh token" });
                }
                Console.WriteLine($"Revoked: {tokenRecord.Revoked}, Expires: {tokenRecord.Expires}");
                if (tokenRecord.Revoked)
                    return Unauthorized(new { Error = "Invalid or revoked refresh token" });

                if (tokenRecord.Expires < DateTime.UtcNow)
                    return Unauthorized(new { Error = "Refresh token has expired" });

                // Get user associated with the token
                var user = await _userService.GetUserByIdAsync(tokenRecord.UserId);
                if (user == null || !user.IsActive)
                    return Unauthorized(new { Error = "User not found or inactive" });

                await using var transaction = await _userService.BeginTransactionAsync();

                try
                {
                    // Revoke old refresh token
                    await _userService.RevokeRefreshToken(refreshToken);

                    // Generate new access and refresh tokens
                    var newAccessToken = await _userService.GenerateAccessToken(user);
                    var newRefreshTokenValue = await _userService.GenerateRefreshToken(user);

                    // Store new refresh token
                    var newRefreshToken = new RefreshToken
                    {
                        Id = Guid.NewGuid(),
                        Token = newRefreshTokenValue,
                        Expires = DateTime.UtcNow.AddDays(7),
                        UserId = user.Id,
                        Revoked = false
                    };
                    await _userService.StoreRefreshTokenAsync(newRefreshToken);

                    // Set new refresh token in cookies
                       Response.Cookies.Append("refreshToken", newRefreshTokenValue, new CookieOptions
                    {
                        HttpOnly = true,                   
                        Secure = true,                      
                        SameSite = SameSiteMode.None,        
                        Domain = ".play929.com",             
                        Expires = DateTime.UtcNow.AddDays(7),
                        Path = "/"                            
                    });

                    // Log the token refresh
                    await _securityLogService.LogSecurityEventAsync(new SecurityLog
                    {
                        UserId = user.Id,
                        IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP",
                        Action = "RefreshToken",
                        Description = "Refresh token used to generate new access token",
                        Timestamp = DateTime.UtcNow
                    });

                    await transaction.CommitAsync();

                    return Ok(new { accessToken = newAccessToken });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error refreshing token for UserId: {UserId}", user.Id);
                    return StatusCode(500, new { Error = "Failed to refresh token" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating refresh token");
                return StatusCode(500, new { Error = "Failed to process refresh token" });
            }
        }

        [Authorize]
        [HttpPost("revoke")]
        public async Task<IActionResult> Revoke()
        {
            var refreshToken = Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
                return BadRequest(new { Error = "Refresh token is missing" });

            bool result = await _userService.RevokeRefreshToken(refreshToken);

            Response.Cookies.Delete("refreshToken", new CookieOptions
            {
                Domain = _config["Jwt:Domain"],
                Secure = true,
                HttpOnly = true,
                SameSite = SameSiteMode.Strict
            });

            return result ? Ok(new { Message = "Session ended." }) : NotFound(new { Error = "Token not found." });
        }


[HttpGet("/verify-email")]
public async Task<IActionResult> Verify([FromQuery] string token)
{
    // Log input
    _logger.LogInformation("Received verify-email request with token: {Token}", token);

    if (string.IsNullOrWhiteSpace(token))
    {
        _logger.LogWarning("Token is null or whitespace");
        return Redirect("/verify-email-fail.html?message=Missing+token");
    }

    using var transaction = await _context.Database.BeginTransactionAsync();
    try
    {
        // Query for token
        _logger.LogInformation("Querying AccountVerificationTokens for token: {Token}", token);
        var dbtoken = await _context.AccountVerificationTokens
            .FirstOrDefaultAsync(t =>
                t.Token == token &&
                !t.Used &&
                t.ExpiresAt > DateTime.UtcNow);

        if (dbtoken == null)
        {
            _logger.LogWarning("No valid token found. Token: {Token}, Current UTC: {UtcNow}", token, DateTime.UtcNow);
            return Redirect("/verify-email-fail.html?message=Invalid+or+expired+token");
        }

        _logger.LogInformation("Found token: UserId={UserId}, ExpiresAt={ExpiresAt}, Used={Used}", 
            dbtoken.UserId, dbtoken.ExpiresAt, dbtoken.Used);

        // Query for user
        _logger.LogInformation("Querying Users for UserId: {UserId}", dbtoken.UserId);
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == dbtoken.UserId);

        if (user == null)
        {
            _logger.LogWarning("No user found for UserId: {UserId}", dbtoken.UserId);
            return Redirect("/verify-email-fail.html?message=User+not+found");
        }

        _logger.LogInformation("Found user: Id={Id}, Email={Email}, IsEmailVerified={IsEmailVerified}, IsActive={IsActive}", 
            user.Id, user.Email, user.IsEmailVerified, user.IsActive);

        // Log entity states before updates
        _logger.LogInformation("Before updates - User state: {UserState}, Token state: {TokenState}", 
            _context.Entry(user).State, _context.Entry(dbtoken).State);

        // Update states
        user.IsEmailVerified = true;
        user.IsActive = true;
        dbtoken.Used = true;

        // Log entity states after updates
        _logger.LogInformation("After updates - User state: {UserState}, Token state: {TokenState}, " +
            "IsEmailVerified={IsEmailVerified}, IsActive={IsActive}, Used={Used}", 
            _context.Entry(user).State, _context.Entry(dbtoken).State, 
            user.IsEmailVerified, user.IsActive, dbtoken.Used);

        // Save changes
        _logger.LogInformation("Calling SaveChangesAsync");
        var changes = await _context.SaveChangesAsync();
        _logger.LogInformation("SaveChangesAsync completed. Number of changes saved: {Changes}", changes);

        // Commit transaction
        _logger.LogInformation("Committing transaction");
        await transaction.CommitAsync();
        _logger.LogInformation("Transaction committed successfully");

        // Non-critical operations (moved outside transaction)
        _logger.LogInformation("Generating access token for user: {UserId}", user.Id);
        var accessToken = await _userService.GenerateAccessToken(user);
        var dashboardLink = $"https://dashboard.play929.com/?sid={accessToken}";
        _logger.LogInformation("Access token generated. Dashboard link: {DashboardLink}", dashboardLink);

        // Send welcome email
        _logger.LogInformation("Sending welcome email to: {Email}", user.Email);
        try
        {
            await _emailService.SendTemplateEmailAsync(
                toEmail: user.Email,
                template: EmailTemplate.Notification,
                templateData: new
                {
                    FullName = user.FullNames,
                    dashboardLink,
                    Message = "Your email has been successfully verified and received our R20 signup bonus."
                },
                subject: "Welcome to Play929!"
            );
            _logger.LogInformation("Welcome email sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send welcome email to {Email}. Continuing execution.", user.Email);
        }

        // Generate and store refresh token
        _logger.LogInformation("Generating refresh token for user: {UserId}", user.Id);
        var refreshTokenValue = await _userService.GenerateRefreshToken(user);
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            Token = refreshTokenValue,
            Expires = DateTime.UtcNow.AddDays(7),
            UserId = user.Id,
            Revoked = false
        };

        _logger.LogInformation("Storing refresh token for user: {UserId}", user.Id);
        await _userService.StoreRefreshTokenAsync(refreshToken);
        _logger.LogInformation("Refresh token stored successfully");

        // Set cookie
        _logger.LogInformation("Setting refreshToken cookie");
        Response.Cookies.Append("refreshToken", refreshTokenValue, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Domain = ".play929.com",
            Expires = DateTime.UtcNow.AddDays(7),
            Path = "/"
        });
        _logger.LogInformation("refreshToken cookie set successfully");

        // Redirect to success page
        _logger.LogInformation("Redirecting to success page for user: {UserId}", user.Id);
        return Redirect($"/verify-email-success.html?name={Uri.EscapeDataString(user.FullNames)}&link={Uri.EscapeDataString(dashboardLink)}");
    }
    catch (Exception ex)
    {
        // Log detailed exception
        _logger.LogError(ex, "Error verifying email with token: {Token}. Inner exception: {InnerException}", 
            token, ex.InnerException?.Message);
        await transaction.RollbackAsync();
        _logger.LogInformation("Transaction rolled back due to error");
        return Redirect("/verify-email-fail.html?message=Unexpected+error");
    }
}



        [Authorize]
        [HttpGet("data")]
        public async Task<IActionResult> GetData()
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { error = "Email claim missing in token" });

            try
            {
                var user = await _userService.GetUserByEmailAsync(email);

                if (user == null)
                    return NotFound(new { error = "Data not found" });

            
                return Ok(new { user.Id, user.FullNames, user.Surname, user.Email, user.PhoneNumber, user.AccountNumber });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Data retrieval failed");
                return StatusCode(500, new { error = "Failed to retrieve Data" });
            }
        }

        [Authorize]
        [HttpGet("logs")]
        public async Task<IActionResult> GetSecurityLogs()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null)
                return Unauthorized(new { error = "User ID claim missing in token" });

            if (!int.TryParse(userIdClaim.Value, out int userId) || userId <= 0)
                return BadRequest(new { error = "Invalid User ID" });

            try
            {
                var logs = await _securityLogService.GetSecurityLogsAsync(userId);
                if (logs == null)
                    return NotFound(new { error = "No security logs found for this user" });

                return Ok(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve security logs for UserId: {UserId}", userId);
                return StatusCode(500, new { error = "Failed to retrieve security logs" });
            }
        }

        private async Task<IActionResult> HandleSuccessfulLogin(User user)
        {
            await using var transaction = await _userService.BeginTransactionAsync();

            try
            {
                var accessToken = await _userService.GenerateAccessToken(user);
                var refreshTokenValue = await _userService.GenerateRefreshToken(user);

                var refreshToken = new RefreshToken
                {
                    Id = Guid.NewGuid(),
                    Token = refreshTokenValue,
                    Expires = DateTime.UtcNow.AddDays(7),
                    UserId = user.Id,
                    Revoked = false
                };

                await _userService.StoreRefreshTokenAsync(refreshToken);

             Response.Cookies.Append("refreshToken", refreshTokenValue, new CookieOptions
                {
                    HttpOnly = true,                   
                    Secure = true,                      
                    SameSite = SameSiteMode.None,        
                    Domain = ".play929.com",             
                    Expires = DateTime.UtcNow.AddDays(7),
                    Path = "/"                            
                });



                await transaction.CommitAsync();

                var LoginRedirectLink = $"{RedirectLink}/?sid={accessToken}";

                return Ok(new
                {
                    link = LoginRedirectLink,
                    Message = "Login successful"
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error handling successful login for userId={UserId}", user.Id);
                return StatusCode(500, new { Error = "Login processing failed" });
            }
        }

        private string GenerateSecureToken(int sizeInBytes = 32)
        {
            // Generate cryptographically secure random bytes
            byte[] tokenBytes = RandomNumberGenerator.GetBytes(sizeInBytes);

            // Convert to URL-safe Base64 string
            string token = Convert.ToBase64String(tokenBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');

            return token;
        }

        private static bool IsPasswordStructurallyValid(string password) =>
            !string.IsNullOrWhiteSpace(password) &&
            password.Length >= 8 &&
            password.Any(char.IsUpper) &&
            password.Any(char.IsLower) &&
            password.Any(char.IsDigit) &&
            password.Any(ch => !char.IsLetterOrDigit(ch));

        private static string? ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                return "Password must be at least 8 characters";
            if (!password.Any(char.IsUpper))
                return "Password must contain at least one uppercase letter";
            if (!password.Any(char.IsLower))
                return "Password must contain at least one lowercase letter";
            if (!password.Any(char.IsDigit))
                return "Password must contain at least one digit";
            if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
                return "Password must contain at least one special character";
            return null;
        }
    }
}

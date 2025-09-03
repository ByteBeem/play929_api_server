using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Play929Backend.Data;
using Play929Backend.DTOs;
using Play929Backend.Models;
using Play929Backend.Services.Interfaces;
using BCrypt.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Data;
using Microsoft.EntityFrameworkCore.Storage;



namespace Play929Backend.Services.Implementations
{
    public class UserService : IUserService
    {
        private const string LockoutCachePrefix = "Lockout_";
        private const string FailedAttemptsPrefix = "FailedAttempts_";
        private const int MaxFailedAttempts = 5;
        private const int LockoutMinutes = 15;

        private readonly AppDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<UserService> _logger;
        private readonly IConfiguration _config;
        private readonly ISecurityLogService _securityLogService;

        public UserService(
            AppDbContext context,
            IMemoryCache memoryCache,
            ILogger<UserService> logger,
            IConfiguration config,
            ISecurityLogService securityLogService)
        {
            _context = context;
            _cache = memoryCache;
            _logger = logger;
            _config = config;
            _securityLogService = securityLogService;
        }

 public async Task<User> RegisterAsync(User user, IDbContextTransaction transaction = null)
{
    Console.WriteLine($"Starting RegisterAsync for user: {user.Email ?? "null"}");
    bool shouldManageTransaction = transaction == null;
    IDbContextTransaction localTransaction = null;

    try
    {
        Console.WriteLine("Generating security stamp and setting default values");
        // Generate security stamp
        user.SecurityStamp = Guid.NewGuid().ToString();
        
        // Set default values
        user.IsActive = user.IsActive || false;
        user.IsEmailVerified = user.IsEmailVerified || false;
        user.IsLocked = user.IsLocked || false;
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        // Log wallet status
        Console.WriteLine(user.Wallet != null 
            ? $"Wallet exists with address: {user.Wallet.WalletAddress ?? "null"}" 
            : "No wallet attached to user");

        // Set wallet timestamps if exists
        if (user.Wallet != null)
        {
            Console.WriteLine("Setting wallet timestamps");
            user.Wallet.CreatedAt = DateTime.UtcNow;
            user.Wallet.UpdatedAt = DateTime.UtcNow;
        }

        // Begin transaction if none was provided
        if (shouldManageTransaction)
        {
            Console.WriteLine("Beginning new transaction");
            localTransaction = await _context.Database.BeginTransactionAsync();
            transaction = localTransaction;
        }
        else
        {
            Console.WriteLine("Using existing transaction");
        }

        Console.WriteLine("Adding user to context");
        _context.Users.Add(user);
        
        if (user.Wallet != null)
        {
            Console.WriteLine($"Adding wallet to context (UserId: {user.Wallet.UserId}, Address: {user.Wallet.WalletAddress})");
            _context.Wallets.Add(user.Wallet);
        }

        Console.WriteLine("Saving changes to database");
        await _context.SaveChangesAsync();
        Console.WriteLine($"SaveChanges completed (User ID: {user.Id}, Wallet ID: {user.Wallet?.Id})");

        if (shouldManageTransaction && localTransaction != null)
        {
            Console.WriteLine("Committing transaction");
            await localTransaction.CommitAsync();
        }

        Console.WriteLine($"Returning user with ID: {user.Id}");
        return user;
    }
    catch (DbUpdateException ex)
    {
        Console.WriteLine($"DbUpdateException occurred: {ex.Message}");
        if (shouldManageTransaction && localTransaction != null)
        {
            Console.WriteLine("Attempting transaction rollback");
            await localTransaction.RollbackAsync();
        }

        _logger.LogError(ex, "Registration failed for {Email}", user.Email);
        throw new DataException("Registration failed due to a database error");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected exception: {ex.GetType().Name} - {ex.Message}");
        if (shouldManageTransaction && localTransaction != null)
        {
            Console.WriteLine("Attempting transaction rollback due to unexpected exception");
            await localTransaction.RollbackAsync();
        }
        throw;
    }
    finally
    {
        if (shouldManageTransaction && localTransaction != null)
        {
            Console.WriteLine("Disposing transaction");
            await localTransaction.DisposeAsync();
        }
        Console.WriteLine("RegisterAsync completed");
    }
}
        public async Task<AuthenticationResult> AuthenticateAsync(string email, string rawPassword, string? ipAddress = null)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(rawPassword))
                return AuthenticationResult.Failed("Email and password are required");

            email = email.Trim().ToLowerInvariant();

            if (IsAccountLocked(email))
                return AuthenticationResult.Locked($"Account locked. Try again in {LockoutMinutes} minutes");

            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Wallet)
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                SimulatePasswordVerification();
                return AuthenticationResult.Failed("Incorrect email or password");
            }

            if (!user.IsActive)
                return AuthenticationResult.Failed("Account not activated. Please check your email.");

            if (user.IsLocked)
                return AuthenticationResult.Locked("Account is locked. Contact support@play929.com");

            if (!VerifyPassword(rawPassword, user.PasswordHash))
            {
                await HandleFailedLogin(user.Email, user.Id, ipAddress =  "127.0.0.1");
                return AuthenticationResult.Failed("Incorrect email or password");
            }

            if (IsPasswordExpired(user))
                return AuthenticationResult.PasswordExpired("Password expired. Please reset your password.");

            await UpdateLastLoginAsync(user.Id, ipAddress= "127.0.0.1");
            ClearFailedLoginAttempts(user.Email);
            return AuthenticationResult.Success(user);
        }

        public async Task<DateTime?> UpdateLastLoginAsync(int userId, string ipAddress = "127.0.0.1")
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) return null;

                user.LastLogin = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await _securityLogService.LogSecurityEventAsync(new SecurityLog
                {
                    UserId = user.Id,
                    Action = "Login",
                    Timestamp = DateTime.UtcNow,
                    IPAddress = ipAddress,
                    Description = "Successful login"
                });

                await transaction.CommitAsync();
                return user.LastLogin;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to update last login for user {UserId}", userId);
                return null;
            }
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email)) return false;
                email = email.Trim().ToLowerInvariant();
                return await _context.Users.AnyAsync(u => u.Email == email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check email existence for {Email}", email);
                return false;
            }
        }

        public async Task<bool> IdNumberExistsAsync(string idNumber)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(idNumber)) return false;
                return await _context.Users.AnyAsync(u => u.IdNumber == idNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check ID number existence for {IdNumber}", idNumber);
                return false;
            }
        }

       private async Task SaveEmailVerificationTokenAsync(int userId, string token, int expiryHours = 24)
        {
            if (userId <= 0)
                throw new ArgumentException("User ID must be a positive integer.", nameof(userId));

            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Token cannot be null or empty.", nameof(token));

            if (expiryHours <= 0)
                throw new ArgumentException("Expiry hours must be greater than zero.", nameof(expiryHours));

            try
            {
               
                var existingTokens = await _context.AccountVerificationTokens
                    .Where(t => t.UserId == userId && !t.Used && t.ExpiresAt > DateTime.UtcNow)
                    .ToListAsync();

                foreach (var t in existingTokens)
                {
                    t.Used = true;
                }

                var verificationToken = new AccountVerificationToken
                {
                    UserId = userId,
                    Token = token,
                    Used = false,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddHours(expiryHours)
                };

                _context.AccountVerificationTokens.Add(verificationToken);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to save email verification token.", ex);
            }
        }


        public async Task<string> GenerateAccessToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_config["Jwt:Secret"] ?? throw new InvalidOperationException("JWT secret key missing"));

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email ?? ""),
                    new Claim(ClaimTypes.Role , user.Role)
                }),
                Expires = DateTime.UtcNow.AddMinutes(5), 
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _config["Jwt:Issuer"],
                Audience = _config["Jwt:Audience"],
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }


       public async Task<User> GetUserByEmailAsync(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("Email cannot be null or empty.", nameof(email));

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);

            return user ?? throw new KeyNotFoundException($"User with email {email} not found.");
        }


          public async Task<string> GenerateRefreshToken(User user)
        {
            byte[] randomBytes = RandomNumberGenerator.GetBytes(32); 
            string refreshToken = Convert.ToBase64String(randomBytes);
            return await Task.FromResult(refreshToken);
        }

      public Task<bool> RevokeRefreshToken(string token)
        {
            // Temporary stub - replace with real logic
            return Task.FromResult(true);
        }


        public async Task<RefreshToken> GetRefreshTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

                Console.WriteLine(token);

            return await _context.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(rt => rt.Token == token && !rt.Revoked);
        }

          public async Task StoreRefreshTokenAsync(RefreshToken refreshToken)
        {
            if (refreshToken == null)
                throw new ArgumentNullException(nameof(refreshToken));

            await _context.RefreshTokens.AddAsync(refreshToken);
            await _context.SaveChangesAsync();
        }

        public async Task<User> GetUserByIdAsync(int userId)
        {
            if (userId <= 0)
                return null;

            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);
        }


        private async Task HandleFailedLogin(string email, int userId, string ipAddress = "127.0.0.1")
        {
            try
            {
                var attemptsKey = FailedAttemptsPrefix + email;
                var failedAttempts = _cache.GetOrCreate(attemptsKey, entry =>
                {
                    entry.SlidingExpiration = TimeSpan.FromMinutes(LockoutMinutes);
                    return 0;
                });

                failedAttempts++;
                _cache.Set(attemptsKey, failedAttempts);

                if (failedAttempts >= MaxFailedAttempts)
                {
                    var lockoutKey = LockoutCachePrefix + email;
                    _cache.Set(lockoutKey, true, TimeSpan.FromMinutes(LockoutMinutes));
                    _cache.Remove(attemptsKey);
                }

                await _securityLogService.LogSecurityEventAsync(new SecurityLog
                {
                    UserId = userId,
                    Action = "FailedLogin",
                    Timestamp = DateTime.UtcNow,
                    IPAddress = ipAddress,
                    Description = $"Failed login attempt #{failedAttempts}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process failed login for {Email}", email);
            }
        }

        private void ClearFailedLoginAttempts(string email)
        {
            _cache.Remove(FailedAttemptsPrefix + email);
        }

        private bool IsAccountLocked(string email)
        {
            return _cache.TryGetValue(LockoutCachePrefix + email, out bool locked) && locked;
        }

       private bool IsPasswordExpired(User user)
        {
            var maxDays = _config.GetValue<int>("Security:PasswordMaxAgeDays", 90);

            if (!user.LastPasswordChange.HasValue)
            {
                
                return true;
            }

            var daysSinceChange = (DateTime.UtcNow - user.LastPasswordChange.Value).TotalDays;
            return daysSinceChange > maxDays;
        }


        public string HashPassword(string password)
        {
            
            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        private bool VerifyPassword(string rawPassword, string hashedPassword)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(rawPassword, hashedPassword);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid password hash format");
                return false;
            }
        }



       private void SimulatePasswordVerification()
        {
            try
            {
                BCrypt.Net.BCrypt.Verify("dummyPassword", "$2a$12$SIMULATEDHASH00000000000000");
            }
            catch
            {
                // intentionally ignored
            }
        }

       public async Task<string> GenerateAccountNumberAsync()
    {
        const string prefix = "PLY-";
        const int maxAttempts = 100;
        int attempts = 0;
        
        while (attempts < maxAttempts)
        {
            var number = Random.Shared.Next(1000, 9999);
            var accNum = $"{prefix}{number}";
            
            bool exists = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.AccountNumber == accNum);
                
            if (!exists)
            {
                return accNum;
            }
            
            attempts++;
            await Task.Delay(10);
        }
        
        _logger.LogError("Failed to generate unique account number after {MaxAttempts} attempts", maxAttempts);
        throw new DataException("Account number generation failed after maximum attempts");
    }

        public async Task AssignRoleAsync(string userId, string role)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(role))
                    throw new ArgumentException("User ID and role are required");

                // Implement your role assignment logic here
                // e.g., insert into UserRoles table
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to assign role {Role} to user {UserId}", role, userId);
                throw new DataException("User role assignment failed");
            }
        }

     public async Task<IDbContextTransaction> BeginTransactionAsync()
        {
            return await _context.Database.BeginTransactionAsync();
        }

        public async Task<bool?> ResetPasswordAsync(int userId, string newPassword, string securityToken)
        {
            // Implement with:
            // 1. Token validation
            // 2. Hash password
            // 3. Update DB + security stamp
            throw new NotImplementedException();
        }

        public async Task InvalidateAllSessionsAsync(int userId)
        {
            // Rotate security stamp + revoke refresh tokens
            throw new NotImplementedException();
        }
    }
}

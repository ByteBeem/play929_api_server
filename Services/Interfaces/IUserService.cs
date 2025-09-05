using Play929Backend.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Play929Backend.DTOs;
using Microsoft.EntityFrameworkCore.Storage;


namespace Play929Backend.Services.Interfaces
{
    public interface IUserService
    {
        Task<User> RegisterAsync(User user, IDbContextTransaction transaction);
        Task<AuthenticationResult> AuthenticateAsync(string email, string rawPassword, string? ipAddress = null);
        Task<bool> EmailExistsAsync(string email);
        Task<DateTime?> UpdateLastLoginAsync(int userId, string? ipAddress = null);
        Task<string> GenerateAccountNumberAsync();
        Task<IDbContextTransaction> BeginTransactionAsync();
        Task<bool> IdNumberExistsAsync(string idNumber);
        Task<bool?> ResetPasswordAsync(int userId, string newPassword, string securityToken);
        Task InvalidateAllSessionsAsync(int userId);
        Task AssignRoleAsync(string userId , string role);
        Task<string> GenerateAccessToken(User user);
        Task<string> GenerateRefreshToken(User user);
        string HashPassword(string password);  
        Task<bool> RevokeRefreshToken(string token);
        Task<User> GetUserByEmailAsync(string email);
        Task<RefreshToken> GetRefreshTokenAsync(string token);
        Task StoreRefreshTokenAsync(RefreshToken refreshToken);
        Task<User> GetUserByIdAsync(int userId);
        Task SaveEmailVerificationTokenAsync(int userId, string token, int expiryHours = 24);
    
    }
}

using Play929Backend.Data;
using Play929Backend.Models;
using Play929Backend.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace Play929Backend.Services.Implementations
{
    public class SecurityLogService : ISecurityLogService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<SecurityLogService> _logger;

        public SecurityLogService(AppDbContext context, ILogger<SecurityLogService> logger)
        {
            _context = context;
            _logger = logger;
        }

        
        public async Task LogSecurityEventAsync(SecurityLog securityLog)
        {
            if (securityLog == null)
                throw new ArgumentNullException(nameof(securityLog));

            try
            {
                _context.SecurityLogs.Add(securityLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log security event");
                throw;
            }
        }

       public async Task<List<SecurityLog>> GetSecurityLogsAsync(int userId)
        {
            if (userId <= 0)
                throw new ArgumentException("Invalid userId", nameof(userId));

            try
            {
                return await _context.SecurityLogs
                    .Where(log => log.UserId == userId)
                    .OrderByDescending(log => log.CreatedAt) 
                    .Take(10)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve security logs for userId: {UserId}", userId);
                throw;
            }
        }


      
    }
}

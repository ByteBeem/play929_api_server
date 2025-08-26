using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Play929Backend.Data;
using Play929Backend.Models;
using Play929Backend.Services.Interfaces;

namespace Play929Backend.Services.Implementations
{
    public class AuditService : IAuditService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AuditService> _logger;

        public AuditService(AppDbContext context, ILogger<AuditService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task LogFinancialEventAsync(FinancialAudit audit)
        {
            try
            {
                if (audit == null)
                {
                    throw new ArgumentNullException(nameof(audit));
                }

                await _context.FinancialAudits.AddAsync(audit);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log financial audit event for wallet: {Wallet}", audit.WalletAddress);
                // Optionally, you can rethrow or suppress the exception depending on your policy.
            }
        }
    }
}

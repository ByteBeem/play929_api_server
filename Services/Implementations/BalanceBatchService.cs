using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Play929Backend.Data;
using Play929Backend.Hubs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Play929Backend.Services.Implementations
{
    public class BalanceBatchService : IHostedService, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BalanceBatchService> _logger;
        private Timer _timer;
        private const int FlushIntervalMs = 5000;

        public BalanceBatchService(IServiceProvider serviceProvider, ILogger<BalanceBatchService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _timer = new Timer(async _ => await FlushPendingBalances(), null, FlushIntervalMs, FlushIntervalMs);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        private async Task FlushPendingBalances()
        {
            foreach (var kvp in CupHub.GetGameStates())
            {
                string sessionToken = kvp.Key;
                var gameState = kvp.Value;

                lock (gameState.Lock)
                {
                    if (gameState.PendingBalanceChange == 0) continue;
                }

                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var session = await dbContext.GameSessions
                    .Include(s => s.User)
                    .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);
                if (session == null) continue;

                var wallet = await dbContext.Wallets.FirstOrDefaultAsync(w => w.UserId == session.UserId);
                if (wallet == null) continue;

                lock (gameState.Lock)
                {
                    wallet.Balance += gameState.PendingBalanceChange;
                    gameState.PendingBalanceChange = 0;
                    gameState.RoundCount = 0;
                    gameState.LastBetAmount = 0;
                }

                try
                {
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation("Batch flush committed for session {SessionToken}. New balance={NewBalance}",
                        sessionToken, wallet.Balance);
                }
                catch (DbUpdateException ex)
                {
                    _logger.LogError(ex, "Failed batch flush for session {SessionToken}.", sessionToken);
                }
            }
        }
    }
}

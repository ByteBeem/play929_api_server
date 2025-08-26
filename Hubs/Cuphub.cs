using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Play929Backend.Data;
using Play929Backend.Models;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Play929Backend.Hubs
{
    public class CupHub : Hub
    {
        private readonly ILogger<CupHub> _logger;
        private readonly AppDbContext _dbContext;

        private static readonly ConcurrentDictionary<string, GameState> _gameStates
            = new ConcurrentDictionary<string, GameState>();

        private const int BatchSize = 10;

        public CupHub(AppDbContext dbContext, ILogger<CupHub> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public class GameState
        {
            public int BallCupIndex { get; set; }
            public int RoundCount { get; set; }
            public decimal PendingBalanceChange { get; set; }
            public decimal LastBetAmount { get; set; }
            public Wallet Wallet { get; set; }
            public string ConnectionId { get; set; } 
            public readonly object Lock = new object();
        }

        public static ConcurrentDictionary<string, GameState> GetGameStates() => _gameStates;

        public async Task PlaceBet(decimal betAmount)
        {
            var sessionToken = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            if (string.IsNullOrEmpty(sessionToken))
            {
                await Clients.Caller.SendAsync("Error", "Invalid session token.");
                return;
            }

            if (betAmount <= 0)
            {
                await Clients.Caller.SendAsync("Error", "Bet amount must be positive.");
                return;
            }

            var session = await _dbContext.GameSessions
               
                .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

            if (session == null)
            {
                await Clients.Caller.SendAsync("Error", "Session not found.");
                return;
            }

            Wallet wallet;
            GameState gameState = null;

            if (_gameStates.TryGetValue(sessionToken, out var existingState))
            {
                if (existingState.ConnectionId != Context.ConnectionId)
                {
                    await Clients.Caller.SendAsync("Error", "Session in use on another connection.");
                    return;
                }
                wallet = existingState.Wallet;
            }
            else
            {
                wallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.UserId == session.UserId);
            }

            if (wallet == null || wallet.Balance < betAmount)
            {
                await Clients.Caller.SendAsync("Error", "Insufficient balance.");
                return;
            }

            var ballCupIndex = RandomNumberGenerator.GetInt32(0, 3);
            var swaps = GenerateShuffle();

            _gameStates.AddOrUpdate(sessionToken,
                new GameState
                {
                    BallCupIndex = ballCupIndex,
                    RoundCount = 1,
                    PendingBalanceChange = -betAmount,
                    LastBetAmount = betAmount,
                    Wallet = wallet,
                    ConnectionId = Context.ConnectionId
                },
                (key, old) =>
                {
                    lock (old.Lock)
                    {
                        if (old.ConnectionId != Context.ConnectionId)
                        {
                            // Log and ignore, or throw
                            _logger.LogWarning("Connection ID mismatch for session {SessionToken}", sessionToken);
                            return old;
                        }
                        old.BallCupIndex = ballCupIndex;
                        old.RoundCount += 1;
                        old.PendingBalanceChange -= betAmount;
                        old.LastBetAmount = betAmount;
                    }
                    return old;
                });

            await Clients.Caller.SendAsync("ShuffleCups", new { swaps });
        }

        public async Task SelectCup(int selectedCupIndex)
        {
            var sessionToken = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            if (string.IsNullOrEmpty(sessionToken) || !_gameStates.TryGetValue(sessionToken, out var gameState))
            {
                await Clients.Caller.SendAsync("Error", "Invalid session or no active game.");
                return;
            }

            if (gameState.ConnectionId != Context.ConnectionId)
            {
                await Clients.Caller.SendAsync("Error", "Session in use on another connection.");
                return;
            }

            var session = await _dbContext.GameSessions
                
                .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);
            if (session == null)
            {
                await Clients.Caller.SendAsync("Error", "Session not found.");
                return;
            }

            var wallet = gameState.Wallet;
            if (wallet == null)
            {
                await Clients.Caller.SendAsync("Error", "Wallet not found.");
                return;
            }

            bool isWin = selectedCupIndex == gameState.BallCupIndex;
            decimal roundChange = isWin ? gameState.LastBetAmount * 2m : 0m; 
            decimal newBalance;

            lock (gameState.Lock)
            {
                gameState.PendingBalanceChange += roundChange;
                gameState.RoundCount += 1;
                newBalance = wallet.Balance + gameState.PendingBalanceChange;
            }

            if (gameState.RoundCount >= BatchSize)
            {
                await CommitBalanceAsync(sessionToken, wallet, gameState);
            }

            await Clients.Caller.SendAsync("GameResult", new
            {
                isWin,
                newBalance,
                ballCupIndex = gameState.BallCupIndex
            });
        }

        private async Task CommitBalanceAsync(string sessionToken, Wallet wallet, GameState gameState)
        {
            decimal change;
            lock (gameState.Lock)
            {
                if (gameState.PendingBalanceChange == 0) return;

                change = gameState.PendingBalanceChange;
                wallet.Balance += change;
                gameState.PendingBalanceChange = 0;
                gameState.RoundCount = 0;
                gameState.LastBetAmount = 0m;
            }

            try
            {
                _dbContext.Update(wallet); 
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Balance committed for session {SessionToken}. Change={Change}, New balance={NewBalance}",
                    sessionToken, change, wallet.Balance);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency conflict committing balance for session {SessionToken}.", sessionToken);
                
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to commit balance for session {SessionToken}.", sessionToken);
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var sessionToken = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            if (!string.IsNullOrEmpty(sessionToken) && _gameStates.TryGetValue(sessionToken, out var gameState))
            {
                if (gameState.ConnectionId == Context.ConnectionId)
                {
                    await CommitBalanceAsync(sessionToken, gameState.Wallet, gameState);
                    _gameStates.TryRemove(sessionToken, out _);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }

        private int[][] GenerateShuffle()
        {
            var swaps = new[] { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 0, 2 }, new[] { 1, 0 }, new[] { 2, 1 }, new[] { 0, 1 } };
            for (int i = swaps.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(0, i + 1);
                var temp = swaps[i];
                swaps[i] = swaps[j];
                swaps[j] = temp;
            }
            return swaps;
        }
    }
}
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

        public override async Task OnConnectedAsync()
        {
            var sessionToken = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            if (!string.IsNullOrEmpty(sessionToken))
            {
                _logger.LogInformation("Client connected with sessionToken: {SessionToken}, ConnectionId: {ConnectionId}", 
                    sessionToken, Context.ConnectionId);

                var session = await _dbContext.GameSessions
                    .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

                if (session != null && _gameStates.TryGetValue(sessionToken, out var gameState))
                {
                    lock (gameState.Lock)
                    {
                        _logger.LogInformation("Updating ConnectionId for sessionToken: {SessionToken} from {OldConnectionId} to {NewConnectionId}", 
                            sessionToken, gameState.ConnectionId, Context.ConnectionId);
                        gameState.ConnectionId = Context.ConnectionId;
                    }
                }
            }
            await base.OnConnectedAsync();
        }

        public async Task PlaceBet(decimal betAmount)
        {
            var sessionToken = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            _logger.LogInformation("PlaceBet called with sessionToken: {SessionToken}, betAmount: {BetAmount}, ConnectionId: {ConnectionId}", 
                sessionToken, betAmount, Context.ConnectionId);

            if (string.IsNullOrEmpty(sessionToken))
            {
                _logger.LogWarning("Invalid session token.");
                await Clients.Caller.SendAsync("Error", "Invalid session token.");
                return;
            }

            if (betAmount <= 0)
            {
                _logger.LogWarning("Bet amount must be positive: {BetAmount}", betAmount);
                await Clients.Caller.SendAsync("Error", "Bet amount must be positive.");
                return;
            }

            var session = await _dbContext.GameSessions
                .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

            if (session == null)
            {
                _logger.LogWarning("Session not found for sessionToken: {SessionToken}", sessionToken);
                await Clients.Caller.SendAsync("Error", "Session not found.");
                return;
            }

            Wallet wallet;
            GameState gameState;

            if (_gameStates.TryGetValue(sessionToken, out var existingState))
            {
                lock (existingState.Lock)
                {
                    if (existingState.ConnectionId != Context.ConnectionId)
                    {
                        _logger.LogInformation("Updating ConnectionId for sessionToken: {SessionToken} from {OldConnectionId} to {NewConnectionId}", 
                            sessionToken, existingState.ConnectionId, Context.ConnectionId);
                        existingState.ConnectionId = Context.ConnectionId;
                    }
                    gameState = existingState;
                    wallet = gameState.Wallet;
                }
            }
            else
            {
                wallet = await _dbContext.Wallets.FirstOrDefaultAsync(w => w.UserId == session.UserId);
                if (wallet == null)
                {
                    _logger.LogWarning("Wallet not found for UserId: {UserId}", session.UserId);
                    await Clients.Caller.SendAsync("Error", "Wallet not found.");
                    return;
                }

                gameState = new GameState
                {
                    Wallet = wallet,
                    ConnectionId = Context.ConnectionId
                };
            }

            if (wallet.Balance < betAmount)
            {
                _logger.LogWarning("Insufficient balance for UserId: {UserId}, Balance: {Balance}, BetAmount: {BetAmount}", 
                    session.UserId, wallet.Balance, betAmount);
                await Clients.Caller.SendAsync("Error", "Insufficient balance.");
                return;
            }

            var ballCupIndex = RandomNumberGenerator.GetInt32(0, 3);
            var swaps = GenerateShuffle();

            _gameStates.AddOrUpdate(sessionToken,
                gameState,
                (key, old) =>
                {
                    lock (old.Lock)
                    {
                        old.BallCupIndex = ballCupIndex;
                        old.RoundCount += 1;
                        old.PendingBalanceChange -= betAmount;
                        old.LastBetAmount = betAmount;
                        old.ConnectionId = Context.ConnectionId;
                    }
                    return old;
                });

            _logger.LogInformation("Bet placed for sessionToken: {SessionToken}, BallCupIndex: {BallCupIndex}, RoundCount: {RoundCount}", 
                sessionToken, ballCupIndex, gameState.RoundCount);

            await Clients.Caller.SendAsync("ShuffleCups", new { swaps });
        }

        public async Task SelectCup(int selectedCupIndex)
        {
            var sessionToken = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            _logger.LogInformation("SelectCup called with sessionToken: {SessionToken}, selectedCupIndex: {SelectedCupIndex}, ConnectionId: {ConnectionId}", 
                sessionToken, selectedCupIndex, Context.ConnectionId);

            if (string.IsNullOrEmpty(sessionToken) || !_gameStates.TryGetValue(sessionToken, out var gameState))
            {
                _logger.LogWarning("Invalid session or no active game for sessionToken: {SessionToken}", sessionToken);
                await Clients.Caller.SendAsync("Error", "Invalid session or no active game.");
                return;
            }

            lock (gameState.Lock)
            {
                if (gameState.ConnectionId != Context.ConnectionId)
                {
                    _logger.LogInformation("Updating ConnectionId for sessionToken: {SessionToken} from {OldConnectionId} to {NewConnectionId}", 
                        sessionToken, gameState.ConnectionId, Context.ConnectionId);
                    gameState.ConnectionId = Context.ConnectionId;
                }
            }

            var session = await _dbContext.GameSessions
                .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);
            if (session == null)
            {
                _logger.LogWarning("Session not found for sessionToken: {SessionToken}", sessionToken);
                await Clients.Caller.SendAsync("Error", "Session not found.");
                return;
            }

            var wallet = gameState.Wallet;
            if (wallet == null)
            {
                _logger.LogWarning("Wallet not found for sessionToken: {SessionToken}", sessionToken);
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

            _logger.LogInformation("Game result for sessionToken: {SessionToken}, isWin: {IsWin}, newBalance: {NewBalance}, ballCupIndex: {BallCupIndex}", 
                sessionToken, isWin, newBalance, gameState.BallCupIndex);

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
                _logger.LogInformation("Balance committed for sessionToken: {SessionToken}, Change: {Change}, New balance: {NewBalance}", 
                    sessionToken, change, wallet.Balance);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogError(ex, "Concurrency conflict committing balance for sessionToken: {SessionToken}", sessionToken);
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Failed to commit balance for sessionToken: {SessionToken}", sessionToken);
                throw;
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var sessionToken = Context.GetHttpContext()?.Request.Query["access_token"].ToString();
            if (!string.IsNullOrEmpty(sessionToken) && _gameStates.TryGetValue(sessionToken, out var gameState))
            {
                Wallet wallet = null;
                decimal change = 0m;
                lock (gameState.Lock)
                {
                    if (gameState.ConnectionId == Context.ConnectionId)
                    {
                        _logger.LogInformation("Client disconnected for sessionToken: {SessionToken}, ConnectionId: {ConnectionId}", 
                            sessionToken, Context.ConnectionId);
                        wallet = gameState.Wallet;
                        change = gameState.PendingBalanceChange;
                        gameState.PendingBalanceChange = 0;
                        gameState.RoundCount = 0;
                        gameState.LastBetAmount = 0m;
                        _gameStates.TryRemove(sessionToken, out _);
                    }
                }

                if (wallet != null && change != 0)
                {
                    await CommitBalanceAsync(sessionToken, wallet, new GameState { Wallet = wallet, PendingBalanceChange = change });
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
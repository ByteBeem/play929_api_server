using Microsoft.AspNetCore.Mvc;
using Play929Backend.DTOs;
using Play929Backend.Models;
using Play929Backend.Data;
using Play929Backend.Services;
using Play929Backend.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Play929Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly AppDbContext _dbContext;
        private readonly IUserService _userService;
        private readonly ILogger<GameController> _logger;

        public GameController(
            IWebHostEnvironment env,
            IUserService userService,
            ILogger<GameController> logger,
            AppDbContext dbContext)
        {
            _env = env;
            _userService = userService;
            _logger = logger;
            _dbContext = dbContext;
        }

        [Authorize]
        [HttpGet]
        public IActionResult GetGames()
        {
            var games = new List<GameDto>
            {
                new GameDto
                { 
                    Id = 1,
                    Name = "Find the ball under Cup",
                    ImageUrl = Url.Content("~/images/cup_ball.webp")
                },
                new GameDto
                {
                    Id = 2,
                    Name = "Word Search Puzzle",
                    ImageUrl = Url.Content("~/images/word.png")
                }
            };

            return Ok(games);
        }

        [HttpGet("/game/word-search/play")]
        public async Task<IActionResult> PlayWordSearch([FromQuery] string launchToken)
        {
            if (string.IsNullOrWhiteSpace(launchToken))
                return BadRequest("Missing launch token.");

            // Validate token securely
            var token = await _dbContext.GameLaunchTokens
                .Include(l => l.GameSession)
                    .ThenInclude(s => s.User)
                .FirstOrDefaultAsync(l =>
                    l.LaunchToken == launchToken &&
                    !l.Used &&
                    l.ExpiresAt > DateTime.UtcNow
                );

            if (token == null)
                return Unauthorized("Invalid or expired launch token.");

            // Mark as used to prevent replay
            token.Used = true;
            await _dbContext.SaveChangesAsync();

            var filePath = Path.Combine(_env.WebRootPath, "word-search", "index.html");
            if (!System.IO.File.Exists(filePath))
                return NotFound("Game not found");

            // Pass session info to frontend securely via headers
            Response.Headers.Add("X-Game-SessionToken", token.GameSession.SessionToken);
            Response.Headers.Add("X-Game-UserId", token.GameSession.UserId.ToString());

            // Read and inject session token into the HTML before returning
            var htmlContent = System.IO.File.ReadAllText(filePath);

            htmlContent = htmlContent.Replace(
                "<meta name=\"game-session-token\" content=\"{injected-session-token}\">",
                $"<meta name=\"game-session-token\" content=\"{token.GameSession.SessionToken}\">"
            );

            return Content(htmlContent, "text/html");
        }


        [HttpGet("/game/cup-game/play")]
        public async Task<IActionResult> PlayCupGame([FromQuery] string launchToken)
        {
            if (string.IsNullOrWhiteSpace(launchToken))
                return BadRequest("Missing launch token.");

            var token = await _dbContext.GameLaunchTokens
                .Include(l => l.GameSession)
                    .ThenInclude(s => s.User)
                .FirstOrDefaultAsync(l =>
                    l.LaunchToken == launchToken &&
                    !l.Used &&
                    l.ExpiresAt > DateTime.UtcNow
                );

            if (token == null)
                return Unauthorized("Invalid or expired launch token.");

            var wallet = await _dbContext.Wallets
                .FirstOrDefaultAsync(w => w.UserId == token.GameSession.UserId);

            if (wallet == null)
                return NotFound("User wallet not found.");

            token.Used = true;
            await _dbContext.SaveChangesAsync();

            var filePath = Path.Combine(_env.WebRootPath, "CupGame", "index.html");
            if (!System.IO.File.Exists(filePath))
                return NotFound("Game not found");

            var htmlContent = System.IO.File.ReadAllText(filePath);

            var balanceMetaTag = $"<meta name=\"game-balance\" content=\"{wallet.Balance.ToString("F0")}\">";
            htmlContent = htmlContent.Replace(
                "<meta name=\"game-session-token\" content=\"{injected-session-token}\">",
                $"<meta name=\"game-session-token\" content=\"{token.GameSession.SessionToken}\">\n  {balanceMetaTag}"
            );

            // Set response headers
            Response.Headers.Add("X-Game-SessionToken", token.GameSession.SessionToken);
            Response.Headers.Add("X-Game-UserId", token.GameSession.UserId.ToString());
            Response.Headers.Add("X-Game-Balance", wallet.Balance.ToString("F0"));

            // Return the modified HTML content
            return Content(htmlContent, "text/html");
        }

        [Authorize]
        [HttpPost("/game/word-search/start")]
        public async Task<IActionResult> StartWordSearch([FromBody] WordSearchStart start)
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { error = "Email claim missing in token" });

            if (start == null || string.IsNullOrWhiteSpace(start.Difficulty) || start.Amount <= 0)
                return BadRequest(new { error = "Invalid request payload" });

            try
            {
                var user = await _userService.GetUserByEmailAsync(email);
                if (user == null) return NotFound(new { error = "User not found" });

                await using var transaction = await _dbContext.Database.BeginTransactionAsync();

                // Check for existing active session
                var existingSession = await _dbContext.GameSessions
                    .Include(s => s.LaunchTokens)
                    .FirstOrDefaultAsync(s => s.UserId == user.Id && s.IsActive);

                if (existingSession != null)
                {
                    existingSession.IsActive = false;

                    if (existingSession.LaunchTokens != null)
                    {
                        foreach (var token in existingSession.LaunchTokens)
                            token.Used = true;
                    }

                    await _dbContext.SaveChangesAsync();
                }

                // Create new session
                var session = new GameSession
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    GameName = "WordSearch",
                    SessionToken = GenerateSessionToken(),
                    StartedAt = DateTime.UtcNow,
                    IsActive = true,
                   // Amount = start.Amount
                };

                _dbContext.GameSessions.Add(session);
                await _dbContext.SaveChangesAsync();

                // Create single-use launch token
                var launchToken = new GameLaunchToken
                {
                    Id = Guid.NewGuid(),
                    LaunchToken = GenerateLaunchToken(),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(2),
                    Used = false,
                    GameSessionId = session.Id
                };

                _dbContext.GameLaunchTokens.Add(launchToken);
                await _dbContext.SaveChangesAsync();

                await transaction.CommitAsync();

                var gameUrl = $"http://localhost:5245/game/word-search/play?launchToken={Uri.EscapeDataString(launchToken.LaunchToken)}";

                return Ok(new { RedirectUrl = gameUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Word search game initialization failed");
                return StatusCode(500, new { error = "Failed to start game." });
            }
        }


        
        [Authorize]
        [HttpPost("/game/cupGame/start")]
        public async Task<IActionResult> StartCupGame()
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (string.IsNullOrWhiteSpace(email))
                return Unauthorized(new { error = "Email claim missing in token" });

      

            try
            {
                var user = await _userService.GetUserByEmailAsync(email);
                if (user == null) return NotFound(new { error = "User not found" });

                await using var transaction = await _dbContext.Database.BeginTransactionAsync();

                // Check for existing active session
                var existingSession = await _dbContext.GameSessions
                    .Include(s => s.LaunchTokens)
                    .FirstOrDefaultAsync(s => s.UserId == user.Id && s.IsActive);

                if (existingSession != null)
                {
                    existingSession.IsActive = false;

                    if (existingSession.LaunchTokens != null)
                    {
                        foreach (var token in existingSession.LaunchTokens)
                            token.Used = true;
                    }

                    await _dbContext.SaveChangesAsync();
                }

                // Create new session
                var session = new GameSession
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    GameName = "WordSearch",
                    SessionToken = GenerateSessionToken(),
                    StartedAt = DateTime.UtcNow,
                    IsActive = true,
                   // Amount = start.Amount
                };

                _dbContext.GameSessions.Add(session);
                await _dbContext.SaveChangesAsync();

                // Create single-use launch token
                var launchToken = new GameLaunchToken
                {
                    Id = Guid.NewGuid(),
                    LaunchToken = GenerateLaunchToken(),
                    ExpiresAt = DateTime.UtcNow.AddMinutes(2),
                    Used = false,
                    GameSessionId = session.Id
                };

                _dbContext.GameLaunchTokens.Add(launchToken);
                await _dbContext.SaveChangesAsync();

                await transaction.CommitAsync();

                var gameUrl = $"http://localhost:5245/game/cup-game/play?launchToken={Uri.EscapeDataString(launchToken.LaunchToken)}";

                return Ok(new { RedirectUrl = gameUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Word search game initialization failed");
                return StatusCode(500, new { error = "Failed to start game." });
            }
        }


        private static string GenerateSessionToken()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static string GenerateLaunchToken()
        {
            var bytes = new byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }
    }
}

using Microsoft.AspNetCore.SignalR;
using Play929Backend.Hubs;
using System.Timers; 

public class GameTimerService
{
    private readonly IHubContext<GameHub> _hubContext;
    private readonly GameWordService _wordService;
    private readonly Dictionary<string, System.Timers.Timer> _timers = new();

    public GameTimerService(IHubContext<GameHub> hubContext, GameWordService wordService)
    {
        _hubContext = hubContext;
        _wordService = wordService;
    }

    public void StartCountdown(string connectionId, int totalSeconds)
    {
        var secondsLeft = totalSeconds;

        var timer = new System.Timers.Timer(1000); 
        timer.Elapsed += async (sender, args) =>
        {
            try
            {
                // Send the words once at the start
                if (secondsLeft == totalSeconds)
                {
                    await _hubContext.Clients.Client(connectionId)
                        .SendAsync("gameData", new { words = _wordService.GetWordsAsCsv() });
                }

                if (secondsLeft <= 0)
                {
                    timer.Stop();
                    await _hubContext.Clients.Client(connectionId).SendAsync("gameOver");
                }
                else
                {
                    var hours = secondsLeft / 3600;
                    var minutes = (secondsLeft % 3600) / 60;
                    var seconds = secondsLeft % 60;

                    await _hubContext.Clients.Client(connectionId).SendAsync("timeUpdate", new
                    {
                        hours,
                        minutes,
                        seconds
                    });

                    secondsLeft--;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Timer error: {ex}");
            }
        };
        timer.Start();
        _timers[connectionId] = timer;
    }

    public void StopCountdown(string connectionId)
    {
        if (_timers.TryGetValue(connectionId, out var timer))
        {
            timer.Stop();
            timer.Dispose();
            _timers.Remove(connectionId);
        }
    }
}

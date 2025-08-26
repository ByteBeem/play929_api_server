using Microsoft.AspNetCore.SignalR;

namespace Play929Backend.Hubs
{
    public class GameHub : Hub
    {
        private readonly GameTimerService _timerService;

        public GameHub(GameTimerService timerService)
        {
            _timerService = timerService;
        }

        public override Task OnConnectedAsync()
        {
            Console.WriteLine($"User connected: {Context.ConnectionId}");


            _timerService.StartCountdown(Context.ConnectionId, 2 * 60); 

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"User disconnected: {Context.ConnectionId}");

            _timerService.StopCountdown(Context.ConnectionId);

            return base.OnDisconnectedAsync(exception);
        }
    }
}

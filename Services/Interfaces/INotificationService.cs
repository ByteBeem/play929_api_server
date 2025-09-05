using System.Threading.Tasks;
using Play929Backend.Models;  

namespace Play929Backend.Services.Interfaces
{
    public interface INotificationService
    {
        Task CreateAsync(Notification notification);
        // Add more methods as needed, e.g., GetNotificationsAsync, MarkAsReadAsync, etc.
    }
}

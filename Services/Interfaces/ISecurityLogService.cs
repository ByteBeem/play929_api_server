using Play929Backend.Models;
using System.Threading.Tasks;

namespace Play929Backend.Services.Interfaces
{
    public interface ISecurityLogService
    {
        
        Task LogSecurityEventAsync(SecurityLog securityLog);
        Task<List<SecurityLog>> GetSecurityLogsAsync(int userId);
    }
}

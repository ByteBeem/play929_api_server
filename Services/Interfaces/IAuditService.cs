using System.Threading.Tasks;
using Play929Backend.Models;

namespace Play929Backend.Services.Interfaces
{
    public interface IAuditService
    {
        Task LogFinancialEventAsync(FinancialAudit audit);
    }
}

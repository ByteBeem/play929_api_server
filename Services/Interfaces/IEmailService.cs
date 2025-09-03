using System.Threading.Tasks;
using Play929Backend.Models;
using Play929Backend.DTOs;

namespace Play929Backend.Services.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlContent);
         Task SendTemplateEmailAsync(string toEmail, EmailTemplate template, object templateData, string subject = null);
    }
}

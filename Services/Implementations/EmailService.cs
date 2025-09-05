using System;
using System.IO;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Play929Backend.Services.Interfaces;
using Play929Backend.Models;
using Play929Backend.DTOs;


namespace Play929Backend.Services.Implementations
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly IConfiguration _config;
        private readonly string _smtpServer;
        private readonly int _port;
        private readonly string _senderName;
        private readonly string _senderEmail;
        private readonly string _username;
        private readonly string _password;
        private readonly bool _useSSL;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _logger = logger;
            _config = config;

            _smtpServer = _config["EmailSettings:SmtpServer"];
            _port = int.Parse(_config["EmailSettings:Port"]);
            _senderName = _config["EmailSettings:SenderName"];
            _senderEmail = _config["EmailSettings:SenderEmail"];
            _username = _config["EmailSettings:Username"];
            _password = _config["EmailSettings:Password"];
            _useSSL = bool.Parse(_config["EmailSettings:UseSSL"]);
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            try
            {
                var email = new MimeMessage();
                email.From.Add(new MailboxAddress(_senderName, _senderEmail));
                email.To.Add(MailboxAddress.Parse(toEmail));
                email.Subject = subject;

                var bodyBuilder = new BodyBuilder { HtmlBody = htmlContent };
                email.Body = bodyBuilder.ToMessageBody();

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(_smtpServer, _port, _useSSL ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
                await smtp.AuthenticateAsync(_username, _password);
                await smtp.SendAsync(email);
                await smtp.DisconnectAsync(true);

                _logger.LogInformation("Email sent to {Email} successfully", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
                throw;
            }
        }

        public async Task SendTemplateEmailAsync(string toEmail, EmailTemplate template, object templateData, string subject = null)
        {
            try
            {
                string templatePath = template switch
                {
                    EmailTemplate.Welcome => Path.Combine("MailTemplates", "Welcome.html"),
                    EmailTemplate.PasswordReset => Path.Combine("MailTemplates", "PasswordReset.html"),
                    EmailTemplate.Notification => Path.Combine("MailTemplates", "Notification.html"),
                    EmailTemplate.EmailVerify => Path.Combine("MailTemplates", "EmailVerify.html"),
                    _ => throw new ArgumentException("Invalid template")
                };

                if (!File.Exists(templatePath))
                    throw new FileNotFoundException($"Template file not found: {templatePath}");

                string htmlContent = await File.ReadAllTextAsync(templatePath);

                // Replace placeholders dynamically
                foreach (var prop in templateData.GetType().GetProperties())
                {
                    string placeholder = $"{{{{{prop.Name}}}}}";
                    string value = prop.GetValue(templateData)?.ToString() ?? string.Empty;
                    htmlContent = htmlContent.Replace(placeholder, value);
                }

                await SendEmailAsync(toEmail, subject ?? template.ToString(), htmlContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send template email to {Email}", toEmail);
                throw;
            }
        }
    }
}

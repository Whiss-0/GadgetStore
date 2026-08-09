using System.Net;
using System.Net.Mail;

namespace api.Services
{
    public interface IEmailService
    {
        Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string resetToken, CancellationToken ct)
        {
            var fromEmail   = _config["Email:FromAddress"]  ?? throw new InvalidOperationException("Email:FromAddress not configured. Add Email__FromAddress to conn.env.");
            var appPassword = _config["Email:AppPassword"]  ?? throw new InvalidOperationException("Email:AppPassword not configured. Add Email__AppPassword to conn.env.");

            var client = new SmtpClient("smtp.gmail.com", 587)
            {
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromEmail, appPassword),
                EnableSsl   = true
            };

            var message = new MailMessage(fromEmail, toEmail)
            {
                Subject = "Password Reset Code",
                Body    = $"Your reset code is: {resetToken}"
            };

            await client.SendMailAsync(message, ct);
        }
    }
}

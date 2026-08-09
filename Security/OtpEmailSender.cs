using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace api.Security
{
    public interface IOtpEmailSender
    {
        Task SendOtpAsync(string toEmail, string otpCode, CancellationToken ct = default);
    }

    public class SmtpOtpEmailSender : IOtpEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpOtpEmailSender> _logger;

        public SmtpOtpEmailSender(IConfiguration config, ILogger<SmtpOtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendOtpAsync(string toEmail, string otpCode, CancellationToken ct = default)
        {
            var host     = _config["Smtp:Host"]     ?? "smtp.gmail.com";
            var portStr  = _config["Smtp:Port"]     ?? "587";
            var user     = _config["Smtp:Username"] ?? throw new InvalidOperationException("Smtp:Username is not configured.");
            var pass     = _config["Smtp:Password"] ?? throw new InvalidOperationException("Smtp:Password is not configured.");
            var fromName = _config["Smtp:FromName"] ?? "GadgetStore";
            var from     = _config["Smtp:From"]     ?? user;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(fromName, from));
            message.To.Add(new MailboxAddress(toEmail, toEmail));
            message.Subject = "Your Code";
            message.Body = new BodyBuilder
            {
                HtmlBody = $"""
                    <p>Use this code to verify your identity:</p>
                    <h1 style="letter-spacing:6px;">{otpCode}</h1>
                    <p>This code expires in 10 minutes. If you didn't request this, ignore this email.</p>
                    """
            }.ToMessageBody();

            try
            {
                using var client = new SmtpClient();
                await client.ConnectAsync(host, int.Parse(portStr), SecureSocketOptions.StartTls, ct);
                await client.AuthenticateAsync(user, pass, ct);
                await client.SendAsync(message, ct);
                await client.DisconnectAsync(true, ct);
                _logger.LogInformation("OTP email sent to {Email}.", toEmail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP email to {Email}.", toEmail);
                throw;
            }
        }
    }

    // Falls back to logging the code if SMTP fails — lets you keep testing
    // locally even if Gmail rejects the connection for some reason.
    public class DevOtpEmailSender : IOtpEmailSender
    {
        private readonly SmtpOtpEmailSender _smtp;
        private readonly ILogger<DevOtpEmailSender> _logger;

        public DevOtpEmailSender(SmtpOtpEmailSender smtp, ILogger<DevOtpEmailSender> logger)
        {
            _smtp = smtp;
            _logger = logger;
        }

        public async Task SendOtpAsync(string toEmail, string otpCode, CancellationToken ct = default)
        {
            try
            {
                await _smtp.SendOtpAsync(toEmail, otpCode, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[DEV] SMTP failed ({Reason}). OTP for {Email}: {Code}", ex.Message, toEmail, otpCode);
            }
        }
    }
}

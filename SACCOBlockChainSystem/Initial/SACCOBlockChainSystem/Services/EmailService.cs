using System.Net;
using System.Net.Mail;

namespace SACCOBlockChainSystem.Services
{
    public interface IEmailService
    {
        Task<bool> SendVerificationCodeAsync(string email, string username, string code);
        }
        public class EmailService : IEmailService
        {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<bool> SendVerificationCodeAsync(string email, string username, string code)
        {
            try
            {
                var smtpServer = _configuration["EmailSettings:SmtpServer"] ?? "smtp.gmail.com";
                var smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587");
                var smtpUsername = _configuration["EmailSettings:Username"];
                var smtpPassword = _configuration["EmailSettings:Password"];
                var fromEmail = _configuration["EmailSettings:FromEmail"] ?? smtpUsername;

                using var client = new SmtpClient(smtpServer, smtpPort);
                client.EnableSsl = true;
                client.Credentials = new NetworkCredential(smtpUsername, smtpPassword);

                var subject = "Password Reset Verification Code";
                var body = $@"
                    <html>
                    <body style='font-family: Arial, sans-serif;'>
                        <h2>Password Reset Request</h2>
                        <p>Dear {username},</p>
                        <p>You requested to reset your password. Please use the verification code below:</p>
                        <div style='background-color: #f0f0f0; padding: 15px; text-align: center; font-size: 24px; font-weight: bold; letter-spacing: 5px;'>
                            {code}
                        </div>
                        <p>This code will expire in <strong>10 minutes</strong>.</p>
                        <p>If you did not request this, please ignore this email.</p>
                        <hr/>
                        <p style='font-size: 12px; color: #666;'>SACCO Blockchain System</p>
                    </body>
                    </html>";

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };
                mailMessage.To.Add(email);

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation($"Verification code sent to {email}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send email to {email}");
                return false;
            }
        }
    }
}
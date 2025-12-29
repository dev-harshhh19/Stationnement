using System.Net;
using System.Net.Mail;

namespace Stationnement.Web.Services;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
    Task SendVerificationEmailAsync(string email, string token);
    Task SendPasswordResetEmailAsync(string email, string token);
    Task SendBookingConfirmationAsync(string email, string qrCode, string locationName, DateTime startTime, DateTime endTime);
}

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var smtpHost = _configuration["Smtp:Host"];
        var smtpPort = int.Parse(_configuration["Smtp:Port"] ?? "587");
        var smtpUser = _configuration["Smtp:Username"];
        var smtpPass = _configuration["Smtp:Password"];
        var fromEmail = _configuration["Smtp:FromEmail"];
        var fromName = _configuration["Smtp:FromName"];

        using var client = new SmtpClient(smtpHost, smtpPort)
        {
            Credentials = new NetworkCredential(smtpUser, smtpPass),
            EnableSsl = true
        };

        var message = new MailMessage
        {
            From = new MailAddress(fromEmail!, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(to);

        await client.SendMailAsync(message);
    }

    public async Task SendVerificationEmailAsync(string email, string token)
    {
        var subject = "Verify your Stationnement account";
        var body = $@"
            <h2>Welcome to Stationnement!</h2>
            <p>Please verify your email by clicking the link below:</p>
            <p><a href='https://stationnement.fr/verify?token={token}'>Verify Email</a></p>
            <p>This link expires in 24 hours.</p>
        ";
        await SendEmailAsync(email, subject, body);
    }

    public async Task SendPasswordResetEmailAsync(string email, string token)
    {
        var subject = "Reset your Stationnement password";
        var body = $@"
            <h2>Password Reset Request</h2>
            <p>Click the link below to reset your password:</p>
            <p><a href='https://stationnement.fr/reset-password?token={token}'>Reset Password</a></p>
            <p>This link expires in 1 hour. If you didn't request this, please ignore this email.</p>
        ";
        await SendEmailAsync(email, subject, body);
    }

    public async Task SendBookingConfirmationAsync(string email, string qrCode, string locationName, DateTime startTime, DateTime endTime)
    {
        var subject = $"Booking Confirmed - {qrCode}";
        var body = $@"
            <h2>Booking Confirmed!</h2>
            <p>Your parking reservation has been confirmed.</p>
            <table style='margin: 20px 0;'>
                <tr><td><strong>Confirmation Code:</strong></td><td>{qrCode}</td></tr>
                <tr><td><strong>Location:</strong></td><td>{locationName}</td></tr>
                <tr><td><strong>Date:</strong></td><td>{startTime:dddd, MMMM d, yyyy}</td></tr>
                <tr><td><strong>Time:</strong></td><td>{startTime:HH:mm} - {endTime:HH:mm}</td></tr>
            </table>
            <p>Show your QR code at the entrance to access the parking.</p>
        ";
        await SendEmailAsync(email, subject, body);
    }
}

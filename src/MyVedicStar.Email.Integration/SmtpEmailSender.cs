using System.ComponentModel.DataAnnotations;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MyVedicStar.Shared.Exceptions;
using MyVedicStar.Email.Shared;

namespace MyVedicStar.Email.Integration;

public sealed partial class SmtpEmailSender(IOptions<EmailSettings> settings, ILogger<SmtpEmailSender> logger, Func<ISmtpClient> createClient) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail message, CancellationToken token)
    {
        var options = settings.Value;
        if (!options.Enabled) throw new IntegrationException("Messaging is temporarily unavailable. Please try again later.");
        if (!Validator.TryValidateObject(options, new ValidationContext(options), [], true))
            throw new IntegrationException("Messaging is temporarily unavailable. Please try again later.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var client = createClient();
        client.Timeout = options.TimeoutSeconds * 1000;
        try
        {
            var email = new MimeMessage();
            email.From.Add(new MailboxAddress(options.FromName, options.FromEmail));
            email.To.Add(MailboxAddress.Parse(options.ToEmail));
            email.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
            email.Subject = message.Subject;
            email.Body = new TextPart("plain") { Text = message.Body };
            await client.ConnectAsync(options.SmtpHost, options.SmtpPort,
                options.SecurityMode == "StartTls" ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect, timeout.Token);
            await client.AuthenticateAsync(options.Username, options.Password, timeout.Token);
            await client.SendAsync(email, timeout.Token);
            LogAccepted(logger);
            // SMTP has accepted the message. A failed QUIT must not turn success into a retry and duplicate delivery.
            try { await client.DisconnectAsync(true, timeout.Token); }
            catch (Exception) { /* Connection disposal still closes the socket. */ }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Do not log exception messages: SMTP responses can contain addresses or submitted content.
            LogFailed(logger, error.GetType().Name);
            throw new IntegrationException("Your message could not be submitted. Please try again later.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Communication email accepted by SMTP server")]
    private static partial void LogAccepted(ILogger logger);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Communication email delivery failed ({ErrorType})")]
    private static partial void LogFailed(ILogger logger, string errorType);
}

public static class EmailIntegrationRegistration
{
    public static IServiceCollection AddEmailIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailSettings>().Bind(configuration.GetSection(EmailSettings.SectionName))
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<Func<ISmtpClient>>(_ => () => new SmtpClient());
        return services;
    }
}

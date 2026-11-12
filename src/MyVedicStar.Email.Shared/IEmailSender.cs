namespace MyVedicStar.Email.Shared;

public sealed record OutgoingEmail(string ReplyTo, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(OutgoingEmail message, CancellationToken token);
}

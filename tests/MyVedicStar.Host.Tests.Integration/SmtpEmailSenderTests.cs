using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using MyVedicStar.Email.Integration;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;

namespace MyVedicStar.Host.Tests.Integration;

public sealed class SmtpEmailSenderTests
{
    [Theory]
    [InlineData("StartTls", 587, SecureSocketOptions.StartTls)]
    [InlineData("SslOnConnect", 465, SecureSocketOptions.SslOnConnect)]
    public async Task Send_RequiresTls_Authenticates_AndUsesFixedRecipient(string mode, int port, SecureSocketOptions tls)
    {
        var smtp = Substitute.For<ISmtpClient>();
        MimeMessage? delivered = null;
        smtp.SendAsync(Arg.Do<MimeMessage>(message => delivered = message), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>()).Returns("250 accepted");
        var settings = Settings(mode, port);
        var sender = new SmtpEmailSender(Options.Create(settings), NullLogger<SmtpEmailSender>.Instance, () => smtp);
        await sender.SendAsync(new("visitor@example.com", "[MyVedicStar - Feedback] Hello", "<b>plain text</b>"), default);
        await smtp.Received(1).ConnectAsync(settings.SmtpHost, port, tls, Arg.Any<CancellationToken>());
        await smtp.Received(1).AuthenticateAsync(settings.Username, settings.Password, Arg.Any<CancellationToken>());
        Assert.NotNull(delivered);
        Assert.Equal("contact@myvedicstar.com", Assert.IsType<MailboxAddress>(Assert.Single(delivered.To)).Address);
        Assert.Equal(settings.FromEmail, Assert.IsType<MailboxAddress>(Assert.Single(delivered.From)).Address);
        Assert.Equal("visitor@example.com", Assert.IsType<MailboxAddress>(Assert.Single(delivered.ReplyTo)).Address);
        Assert.Equal("<b>plain text</b>", delivered.TextBody);
        Assert.Null(delivered.HtmlBody);
    }

    [Fact]
    public async Task Send_WaitsForAcceptance_AndHidesSmtpFailureDetails()
    {
        var smtp = Substitute.For<ISmtpClient>();
        var acceptance = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        smtp.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>()).Returns(acceptance.Task);
        var sender = new SmtpEmailSender(Options.Create(Settings("StartTls", 587)), NullLogger<SmtpEmailSender>.Instance, () => smtp);
        var send = sender.SendAsync(new("visitor@example.com", "Hello", "Private body"), default);
        Assert.False(send.IsCompleted);
        acceptance.SetException(new IOException("Sensitive SMTP diagnostic"));
        var failure = await Assert.ThrowsAsync<IntegrationException>(() => send);
        Assert.DoesNotContain("Sensitive", failure.ToString());
        Assert.DoesNotContain("Private body", failure.ToString());
    }

    [Fact]
    public async Task Send_Timeout_ReturnsSafeFailure()
    {
        var smtp = Substitute.For<ISmtpClient>();
        smtp.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .Returns(async info => { await Task.Delay(Timeout.Infinite, info.Arg<CancellationToken>()); return "never accepted"; });
        var options = Settings("StartTls", 587); options.TimeoutSeconds = 1;
        var sender = new SmtpEmailSender(Options.Create(options), NullLogger<SmtpEmailSender>.Instance, () => smtp);
        await Assert.ThrowsAsync<IntegrationException>(() => sender.SendAsync(new("visitor@example.com", "Hello", "Body"), default));
    }

    [Fact]
    public async Task Send_DisconnectFailureAfterAcceptance_DoesNotInviteDuplicateRetry()
    {
        var smtp = Substitute.For<ISmtpClient>();
        smtp.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>()).Returns("250 accepted");
        smtp.DisconnectAsync(true, Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("QUIT failed")));
        var sender = new SmtpEmailSender(Options.Create(Settings("StartTls", 587)), NullLogger<SmtpEmailSender>.Instance, () => smtp);
        await sender.SendAsync(new("visitor@example.com", "Hello", "Body"), default);
    }

    private static EmailSettings Settings(string mode, int port) => new()
    {
        Enabled = true, SmtpHost = "smtp.example.com", SmtpPort = port, SecurityMode = mode,
        Username = "mailbox@example.com", Password = "test-only", FromEmail = "mailbox@example.com",
    };
}

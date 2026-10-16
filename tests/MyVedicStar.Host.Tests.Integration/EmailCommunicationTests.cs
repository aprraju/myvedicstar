using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Email.Business;
using MyVedicStar.Email.Integration;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Host.Tests.Integration;

public sealed partial class SharedHostTests
{
    [Theory]
    [InlineData("Feedback")]
    [InlineData("ReportIssue")]
    [InlineData("ContactUs")]
    public async Task Email_AnonymousSubmission_AcceptsOnlyAfterDelivery_AndDeduplicates(string type)
    {
        var sender = new CapturingEmailSender();
        await using var factory = new CombinedFactory();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>(); services.AddSingleton<IEmailSender>(sender);
        }));
        using var client = host.CreateClient();
        var body = new { email = "visitor@example.com", subject = "A question", message = "<script>plain text only</script>", type, reason = "Calendar question" };
        var first = await client.PostAsJsonAsync("/api/v1/email/send", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("\"success\":true", await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/email/send", body)).StatusCode);
        var message = Assert.Single(sender.Messages);
        Assert.Equal("visitor@example.com", message.ReplyTo);
        Assert.Contains(type, message.Subject);
        Assert.Contains("Reason: Calendar question", message.Body);
        Assert.Contains("User ID: Anonymous", message.Body);
        Assert.Contains("First Name: \n", message.Body.Replace("\r\n", "\n"));
        Assert.Contains(body.message, message.Body);
    }

    [Theory]
    [InlineData("bad-email", "Subject", "ContactUs")]
    [InlineData("user@example.com", "Header\r\nBcc: attacker@example.com", "ContactUs")]
    [InlineData("user@example.com", "Subject", "Unknown")]
    public async Task Email_InvalidInput_IsRejectedWithoutDelivery(string email, string subject, string type)
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/email/send", new { email, subject, message = "Hello", type })).StatusCode);
    }

    [Fact]
    public async Task Email_CannotSupplyRecipientOrIdentity_AndRejectsInvalidTokens()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/email/send", new
        { email = "user@example.com", subject = "Subject", message = "Hello", type = "ContactUs", toEmail = "attacker@example.com", userId = "other" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/email/send", new
        { email = "user@example.com", subject = "Subject", message = "Hello", type = "ContactUs" })).StatusCode);
    }

    [Fact]
    public async Task Email_RequiresScopeForSuppliedTokens_AndBoundsRequestFields()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token(scope: null));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/email/send", new
        { email = "user@example.com", subject = "Subject", message = "Hello", type = "ContactUs" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/email/send", new
        { email = "user@example.com", subject = "Subject", message = new string('x', 4001), type = "ContactUs" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/email/send", new
        { email = "user@example.com", subject = "Subject", message = "Hello", type = "ContactUs", reason = new string('x', 201) })).StatusCode);
    }

    [Fact]
    public async Task Email_ValidToken_ReadsRegisteredProfileWithoutCallingLogin()
    {
        var accounts = new EmailAccountFacade(); var sender = new CapturingEmailSender();
        await using var factory = new CombinedFactory();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>(); services.AddSingleton<IEmailSender>(sender);
            services.RemoveAll<IAuthorizeFacade>(); services.AddSingleton<IAuthorizeFacade>(accounts);
        }));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.Token());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/email/send", new
        { email = "reply@example.com", subject = "Subject", message = "Hello", type = "Feedback" })).StatusCode);
        Assert.True(accounts.ReadAuthenticatedIdentity);
        var email = Assert.Single(sender.Messages);
        Assert.Contains(accounts.Id.ToString(), email.Body);
        Assert.Contains("First Name: Asha", email.Body);
        Assert.Contains("Last Name: Rao", email.Body);
        Assert.Contains("Registered Email: registered@example.com", email.Body);
        Assert.DoesNotContain("Bearer", email.Body);
    }

    [Fact]
    public async Task Email_DisabledSmtp_ReturnsSafe503_AndThrottlesAnonymousRequests()
    {
        await using var factory = new CombinedFactory();
        using var client = factory.CreateClient();
        for (var index = 0; index < 5; index++)
        {
            var response = await client.PostAsJsonAsync("/api/email/send", new
            { email = "user@example.com", subject = "Subject", message = "Hello " + index, type = "ContactUs" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("Password", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/email/send", new
        { email = "user@example.com", subject = "Subject", message = "One too many", type = "ContactUs" })).StatusCode);
    }

    [Fact]
    public async Task Email_Deduplication_CoalescesConcurrentCallsAndAllowsRetryAfterFailure()
    {
        using var guard = new EmailSubmissionGuard();
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Task Send() { Interlocked.Increment(ref calls); return accepted.Task; }
        var first = guard.RunAsync("same", Send); var second = guard.RunAsync("same", Send);
        Assert.False(first.IsCompleted); Assert.Equal(1, calls);
        accepted.SetResult(); await Task.WhenAll(first, second);
        await Assert.ThrowsAsync<IOException>(() => guard.RunAsync("failure", () => throw new IOException()));
        await guard.RunAsync("failure", () => { calls++; return Task.CompletedTask; });
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("StartTls", true)]
    [InlineData("SslOnConnect", true)]
    [InlineData("None", false)]
    public void Email_Configuration_RequiresTlsAndFixedRecipient(string securityMode, bool valid)
    {
        var settings = new EmailSettings { Enabled = true, SmtpHost = "smtp.example.com", Username = "user", Password = "not-real", FromEmail = "app@example.com", SecurityMode = securityMode };
        Assert.Equal(valid, System.ComponentModel.DataAnnotations.Validator.TryValidateObject(settings, new(settings), [], true));
        settings.ToEmail = "other@example.com";
        Assert.False(System.ComponentModel.DataAnnotations.Validator.TryValidateObject(settings, new(settings), [], true));
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<OutgoingEmail> Messages { get; } = [];
        public Task SendAsync(OutgoingEmail message, CancellationToken token) { Messages.Add(message); return Task.CompletedTask; }
    }

    private sealed class EmailAccountFacade : IAuthorizeFacade
    {
        public Guid Id { get; } = Guid.NewGuid();
        public bool ReadAuthenticatedIdentity { get; private set; }
        public Task<ProfileResult> GetProfileAsync(ClaimsPrincipal user, CancellationToken token)
        {
            ReadAuthenticatedIdentity = user.Identity?.IsAuthenticated == true;
            return Task.FromResult(new ProfileResult(Id, "Asha", "Rao", "Asha Rao", null, null, null, null, null, null, "registered@example.com", null, 1, false));
        }
        public Task<UserInfo> SignInUserAsync(ClaimsPrincipal user, IPAddress? ip, CancellationToken token) => throw new NotSupportedException();
        public Task<UserInfo> LoginAsync(ClaimsPrincipal user, IPAddress? ip, string? sessionId, CancellationToken token) => throw new NotSupportedException();
        public Task LogoutAsync(ClaimsPrincipal user, IPAddress? ip, string? sessionId, CancellationToken token) => throw new NotSupportedException();
        public Task<ProfileResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileUpdateRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<ProfileResult> RetryProfileSyncAsync(ClaimsPrincipal user, CancellationToken token) => throw new NotSupportedException();
        public Task<Guid> GetRegisteredAccountIdAsync(ClaimsPrincipal user, CancellationToken token) => throw new NotSupportedException();
    }
}

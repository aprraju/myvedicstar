using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Email.Integration;
using MyVedicStar.Shared.Exceptions;

namespace MyVedicStar.Email.Business;

public sealed record CommunicationRequest(string Email, string? PhoneNumber, string Subject, string Message, string Type, string? Reason = null);

public sealed class EmailCommunicationService(IAuthorizeFacade accounts, IEmailSender sender, EmailSubmissionGuard guard)
{
    public async Task SendAsync(CommunicationRequest request, ClaimsPrincipal user, string anonymousKey, CancellationToken token)
    {
        Validate(request);
        ProfileResult? profile = user.Identity?.IsAuthenticated == true ? await accounts.GetProfileAsync(user, token) : null;
        var normalized = request with { Email = request.Email.Trim(), Subject = request.Subject.Trim(), Message = request.Message.Trim(), PhoneNumber = request.PhoneNumber?.Trim(), Reason = request.Reason?.Trim() };
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { Caller = profile?.Id.ToString() ?? anonymousKey, Request = normalized }))));
        await guard.RunAsync(fingerprint, async () =>
        {
            var body = $"""
                MyVedicStar - User Communication

                Communication Type: {normalized.Type}
                Reason: {normalized.Reason ?? ""}
                User ID: {profile?.Id.ToString() ?? "Anonymous"}
                First Name: {profile?.FirstName ?? ""}
                Last Name: {profile?.LastName ?? ""}
                Email: {normalized.Email}
                Registered Email: {profile?.Email ?? ""}
                Phone Number: {normalized.PhoneNumber ?? ""}
                Subject: {normalized.Subject}
                Submitted At: {DateTimeOffset.UtcNow:O}

                Message:
                {normalized.Message}
                """;
            // Plain-text MIME encoding preserves user content without interpreting HTML.
            await sender.SendAsync(new(normalized.Email, $"[MyVedicStar - {normalized.Type}] {normalized.Subject}", body), token);
        });
    }

    public static void Validate(CommunicationRequest request)
    {
        if (!EmailSettings.IsAddress(request.Email?.Trim())) throw new ValidationException("Enter a valid email address.");
        if (request.Type is not ("Feedback" or "ReportIssue" or "ContactUs")) throw new ValidationException("Choose Feedback, ReportIssue, or ContactUs.");
        if (request.Reason is { } reason && (reason.Length > 200 || reason.Any(char.IsControl)))
            throw new ValidationException("Reason must be at most 200 characters without control characters.");
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 200 || request.Subject.Any(char.IsControl))
            throw new ValidationException("Provide a subject of at most 200 characters without control characters.");
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000 || request.Message.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ValidationException("Provide a message of at most 4000 characters.");
        if (request.PhoneNumber is { Length: > 0 } phone && (phone.Length > 40 || phone.Any(c => !char.IsAsciiDigit(c) && c is not ('+' or ' ' or '(' or ')' or '-' or '.')) || phone.Count(char.IsAsciiDigit) is < 7 or > 15))
            throw new ValidationException("Enter a phone number with 7 to 15 digits.");
    }
}

/// <summary>Coalesces repeated identical submissions for one minute per caller, on this API instance.</summary>
public sealed class EmailSubmissionGuard : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private readonly object gate = new();

    public async Task RunAsync(string key, Func<Task> send)
    {
        Lazy<Task> operation;
        lock (gate)
        {
            if (!cache.TryGetValue(key, out Lazy<Task>? existing) || existing is null)
            {
                operation = new Lazy<Task>(send);
                cache.Set(key, operation, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
            }
            else operation = existing;
        }
        try { await operation.Value; }
        catch
        {
            lock (gate)
            {
                if (cache.TryGetValue(key, out Lazy<Task>? current) && ReferenceEquals(current, operation)) cache.Remove(key);
            }
            throw;
        }
    }

    public void Dispose() => cache.Dispose();
}

public static class EmailBusinessRegistration
{
    public static IServiceCollection AddEmailBusiness(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddEmailIntegration(configuration);
        services.AddSingleton<EmailSubmissionGuard>();
        services.AddScoped<EmailCommunicationService>();
        return services;
    }
}

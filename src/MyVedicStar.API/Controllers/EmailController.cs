using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVedicStar.ApiProtection.Auth;
using MyVedicStar.ApiProtection.Abstractions;
using MyVedicStar.Email.Business;

namespace MyVedicStar.API.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SendEmailRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [StringLength(40)] string? PhoneNumber,
    [Required, StringLength(200)] string Subject,
    [Required, StringLength(4000)] string Message,
    [Required] string Type,
    [StringLength(200)] string? Reason = null);

public sealed record SendEmailResponse(bool Success, string Message);

[ApiController, AllowAnonymous, Route("api/email/send"), Route("api/v1/email/send")]
[EnableRateLimiting("email-communication")]
[RequestSizeLimit(32768)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
public sealed class EmailController(EmailCommunicationService communication, IAuthorizationService authorization, IClientIpResolver clientIp) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SendEmailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(400), ProducesResponseType<ProblemDetails>(401), ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(413), ProducesResponseType<ProblemDetails>(429), ProducesResponseType<ProblemDetails>(503)]
    public async Task<IActionResult> Send(SendEmailRequest request, CancellationToken token)
    {
        // Anonymous is supported, but a supplied invalid token must never silently become anonymous.
        if (Request.Headers.ContainsKey("Authorization") && User.Identity?.IsAuthenticated != true)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication failed", detail: "The supplied bearer token is invalid or expired.");
        if (User.Identity?.IsAuthenticated == true && !(await authorization.AuthorizeAsync(User, null, AuthPolicyNames.ApiScope)).Succeeded)
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "The supplied token lacks the required API scope.");
        await communication.SendAsync(new(request.Email, request.PhoneNumber, request.Subject, request.Message, request.Type, request.Reason),
            User, clientIp.Resolve(HttpContext)?.ToString() ?? "unknown", token);
        return Ok(new SendEmailResponse(true, "Your message has been submitted successfully."));
    }
}

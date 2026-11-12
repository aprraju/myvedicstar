using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicStar.Authorize.Business;
using MyVedicStar.Email.Business;
using MyVedicStar.Email.Shared;
using MyVedicStar.Infrastructure.DependencyInjection;
using MyVedicStar.Shared.Exceptions;
using NSubstitute;

namespace MyVedicStar.Email.Tests.Unit;

public sealed class EmailFacadeTests
{
    [Fact]
    public async Task Facade_PreservesNormalizationDeliveryAndDeduplication()
    {
        var sender = Substitute.For<IEmailSender>();
        var accounts = Substitute.For<IAuthorizeFacade>();
        var services = new ServiceCollection().AddLogging();
        services.AddEmailModule(new ConfigurationBuilder().Build());
        services.RemoveAll<IEmailSender>();
        services.AddSingleton(sender);
        services.AddSingleton(accounts);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var facade = scope.ServiceProvider.GetRequiredService<IEmailFacade>();
        var request = new CommunicationRequest(" visitor@example.com ", null, " Question ", " <b>Hello</b> ", "ContactUs");
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        await facade.SendAsync(request, user, "anonymous", CancellationToken.None);
        await facade.SendAsync(request, user, "anonymous", CancellationToken.None);
        await sender.Received(1).SendAsync(Arg.Is<OutgoingEmail>(message =>
            message.ReplyTo == "visitor@example.com" && message.Subject == "[MyVedicStar - ContactUs] Question"
            && message.Body.Contains("User ID: Anonymous") && message.Body.Contains("<b>Hello</b>")), CancellationToken.None);
        await accounts.DidNotReceiveWithAnyArgs().GetProfileAsync(default!, default);
    }

    [Fact]
    public async Task Facade_InvalidSubmissionDoesNotSend()
    {
        var sender = Substitute.For<IEmailSender>();
        using var guard = new EmailSubmissionGuard();
        var facade = new EmailFacade(new EmailCommunicationService(Substitute.For<IAuthorizeFacade>(), sender, guard));
        var error = await Assert.ThrowsAsync<ValidationException>(() => facade.SendAsync(
            new CommunicationRequest("invalid", null, "Subject", "Message", "ContactUs"),
            new ClaimsPrincipal(), "anonymous", CancellationToken.None));
        Assert.Equal("Enter a valid email address.", error.Message);
        await sender.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
    }

    [Fact]
    public void Business_DoesNotReferenceEmailProvider_AndHidesImplementations()
    {
        var assembly = typeof(IEmailFacade).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "MyVedicStar.Email.Integration");
        Assert.False(typeof(EmailFacade).IsPublic);
        Assert.False(typeof(EmailCommunicationService).IsPublic);
        Assert.False(typeof(EmailSubmissionGuard).IsPublic);
    }
}

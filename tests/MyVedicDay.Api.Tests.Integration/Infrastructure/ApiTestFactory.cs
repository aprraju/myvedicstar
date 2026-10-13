using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyVedicStar.ApiProtection.Auth;

namespace MyVedicDay.Api.Tests.Integration.Infrastructure;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces the real JWT authentication
/// schemes with <see cref="TestAuthenticationHandler"/> and overrides the <c>AnyProvider</c>
/// authorization policy so that every test request is treated as authenticated.
/// <para>
/// All other application services, middleware, and configuration remain unchanged so that the
/// integration tests exercise the real pipeline end-to-end.
/// </para>
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Override the default authentication scheme so that all auth challenges and
            // authenticate calls resolve through the test handler, not the real JWT bearer.
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme    = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });

            // Override the AnyProvider policy so that [Authorize(AuthPolicyNames.AnyProvider)]
            // is satisfied by the test scheme above, not by a real provider JWT.
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(TestAuthenticationHandler.SchemeName).RequireAuthenticatedUser().Build();
                options.FallbackPolicy = options.DefaultPolicy;
                options.AddPolicy(AuthPolicyNames.AnyProvider, policy =>
                    policy
                        .AddAuthenticationSchemes(TestAuthenticationHandler.SchemeName)
                        .RequireAuthenticatedUser());
                options.AddPolicy(AuthPolicyNames.ApiScope, policy =>
                    policy.AddAuthenticationSchemes(TestAuthenticationHandler.SchemeName).RequireAuthenticatedUser());
            });
        });
    }
}

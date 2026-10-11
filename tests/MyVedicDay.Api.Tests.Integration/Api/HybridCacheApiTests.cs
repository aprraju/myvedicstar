using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyVedicDay.Api.Tests.Integration.Infrastructure;
using MyVedicDay.Panchang.Calculation;
using MyVedicStar.Caching;
using Microsoft.Extensions.Options;
using MyVedicDay.Panchang.Calculation.Models;
using NodaTime;
using Npgsql;
using Xunit.Abstractions;
namespace MyVedicDay.Api.Tests.Integration.Api;
public sealed class CachePostgresFactAttribute : FactAttribute
{
    public CachePostgresFactAttribute() { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MYVEDICSTAR_TEST_POSTGRES"))) Skip="Set MYVEDICSTAR_TEST_POSTGRES to validate real API cache persistence."; }
}
public sealed class HybridCacheApiTests(ITestOutputHelper output)
{
    private sealed class FrozenClock(Instant now) : IClock { public Instant GetCurrentInstant()=>now; }
    private sealed class CountingCalculator(IPanchangCalculator inner) : IPanchangCalculator
    {
        internal int Calls;
        public PanchangDay Calculate(PanchangCalculationRequest request) { Interlocked.Increment(ref Calls); return inner.Calculate(request); }
    }
    [CachePostgresFact]
    public async Task CachedAndUncachedApis_KeepIdenticalContracts_AndReuseRawPanchangAcrossFeatures()
    {
        var admin=Environment.GetEnvironmentVariable("MYVEDICSTAR_TEST_POSTGRES")!;
        var database="cache_api_"+Guid.NewGuid().ToString("N");
        await using var connection=new NpgsqlConnection(admin);await connection.OpenAsync();
        await new NpgsqlCommand($"CREATE DATABASE {database}",connection).ExecuteNonQueryAsync();
        var cacheConnection=new NpgsqlConnectionStringBuilder(admin){Database=database}.ConnectionString;
        try
        {
            await using(var schema=new NpgsqlConnection(cacheConnection))
            {
                await schema.OpenAsync();
                await new NpgsqlCommand("""CREATE SCHEMA myvedicstar; CREATE TABLE myvedicstar.cache_entries (id VARCHAR(449) COLLATE "C" PRIMARY KEY,value BYTEA NOT NULL,expiresattime TIMESTAMPTZ NOT NULL,slidingexpirationinseconds BIGINT NULL,absoluteexpiration TIMESTAMPTZ NULL); CREATE INDEX ix_cache_entries_expiresattime ON myvedicstar.cache_entries(expiresattime)""",schema).ExecuteNonQueryAsync();
            }
            var now=Instant.FromDateTimeOffset(DateTimeOffset.UtcNow);
            using var factory=new ApiTestFactory();
            WebApplicationFactory<Program> Create(bool enabled)=>factory.WithWebHostBuilder(builder=>
            {
                builder.UseSetting("Caching:Enabled",enabled.ToString());builder.UseSetting("Caching:L2:Enabled",enabled.ToString());
                builder.UseSetting("Database:ConnectionString",cacheConnection);builder.UseSetting("ApiProtection:RateLimiting:Enabled","false");
                builder.ConfigureServices(services=>
                {
                    services.AddSingleton<IClock>(new FrozenClock(now));
                                        if(enabled)
                    {
                        var original=services.Single(d=>d.ServiceType==typeof(IPanchangCalculator));services.RemoveAll<IPanchangCalculator>();
                        services.AddSingleton<IPanchangCalculator>(sp=>new CountingCalculator((IPanchangCalculator)ActivatorUtilities.CreateInstance(sp,original.ImplementationType!)));
                    }
                });
            });
            using var cached=Create(true);using var uncached=Create(false);using var cachedClient=cached.CreateClient();using var uncachedClient=uncached.CreateClient();
            Assert.True(cached.Services.GetRequiredService<IOptions<ApplicationCacheOptions>>().Value.Enabled);
            Assert.False(uncached.Services.GetRequiredService<IOptions<ApplicationCacheOptions>>().Value.Enabled);
            var cachedCalculator = (CountingCalculator)cached.Services.GetRequiredService<IPanchangCalculator>();
            foreach(var path in new[]{"/api/v1/panchang/today?latitude=17.385&longitude=78.4867", "/api/v1/muhurta/good-times?latitude=17.385&longitude=78.4867&activity=general"})
            {
                var beforeFeature = cachedCalculator.Calls;
                var baseline=await Fetch(uncachedClient,path);var first=await Fetch(cachedClient,path);
                var afterCold = cachedCalculator.Calls;
                var warm=await Fetch(cachedClient,path);
                Assert.Equal(afterCold, cachedCalculator.Calls);
                if (path.Contains("good-times", StringComparison.Ordinal)) Assert.Equal(beforeFeature, afterCold);
                Assert.True(JsonNode.DeepEquals(baseline.Payload,first.Payload));Assert.True(JsonNode.DeepEquals(first.Payload,warm.Payload));
                var uncachedTimes=new List<double>();var cachedTimes=new List<double>();
                for(var i=0;i<5;i++){uncachedTimes.Add((await Fetch(uncachedClient,path)).Milliseconds);cachedTimes.Add((await Fetch(cachedClient,path)).Milliseconds);}
                output.WriteLine($"{path}: cold {first.Milliseconds:F2}ms, uncached mean {uncachedTimes.Average():F2}ms, warm mean {cachedTimes.Average():F2}ms; local TestServer, 5 measured requests per mode.");
            }
            Assert.True(cachedCalculator.Calls > 0);
            var before=cachedCalculator.Calls;
            await Task.WhenAll(Enumerable.Range(0,10).Select(_=>Fetch(cachedClient,"/api/v1/panchang/today?latitude=17.385&longitude=78.4867")));
            Assert.Equal(before,cachedCalculator.Calls);
            using var restarted=Create(true);using var restartedClient=restarted.CreateClient();
            await Fetch(restartedClient,"/api/v1/panchang/today?latitude=17.385&longitude=78.4867");Assert.Equal(0,((CountingCalculator)restarted.Services.GetRequiredService<IPanchangCalculator>()).Calls);
            foreach(var path in new[]{"/api/v1/traditions?language=te","/api/v1/panchang/glossary?language=te&tradition=tamil","/api/v1/muhurta/nakshatras?language=te&tradition=telugu"})
            {
                Assert.True(JsonNode.DeepEquals((await Fetch(uncachedClient,path)).Payload,(await Fetch(cachedClient,path)).Payload));
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();await new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)",connection).ExecuteNonQueryAsync();
        }
    }
    private static async Task<(JsonNode? Payload,double Milliseconds)> Fetch(HttpClient client,string path)
    {
        var start=Stopwatch.GetTimestamp();using var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();var json=JsonNode.Parse(await response.Content.ReadAsStringAsync());
        return(json,Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}

using System.Reflection;

namespace MyVedicStar.Email.Tests.Unit;

public sealed class ArchitectureBoundaryTests
{
    [Theory]
    [InlineData("MyVedicStar.Authorize.Business")]
    [InlineData("MyVedicStar.Settings.Business")]
    [InlineData("MyVedicStar.Email.Business")]
    [InlineData("MyVedicDay.Panchang.Business")]
    [InlineData("MyVedicDay.Muhurta.Business")]
    [InlineData("MyVedicDay.Festivals.Business")]
    [InlineData("MyVedicDay.Location.Business")]
    [InlineData("MyVedicDay.Localization.Business")]
    [InlineData("MyVedicDay.Traditions.Business")]
    public void Business_DoesNotDependOnPersistenceIntegrationOrComposition(string assemblyName)
    {
        var names = Assembly.Load(assemblyName).GetReferencedAssemblies().Select(reference => reference.Name ?? "");
        Assert.DoesNotContain(names, name => name.EndsWith(".Data", StringComparison.Ordinal)
            || name.EndsWith(".Integration", StringComparison.Ordinal)
            || name.Contains(".Infrastructure.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Shared_DoesNotDependOnConcreteCacheProvider()
    {
        var names = Assembly.Load("MyVedicStar.Shared").GetReferencedAssemblies().Select(reference => reference.Name);
        Assert.DoesNotContain("MyVedicStar.Caching", names);
    }

    [Fact]
    public void SettingsPersistence_DoesNotDependOnAuthorizePersistence()
    {
        var names = Assembly.Load("MyVedicStar.Settings.Data").GetReferencedAssemblies().Select(reference => reference.Name);
        Assert.DoesNotContain("MyVedicStar.Authorize.Data", names);
    }
}

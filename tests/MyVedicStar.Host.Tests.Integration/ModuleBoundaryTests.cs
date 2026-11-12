using System.Reflection;

namespace MyVedicStar.Host.Tests.Integration;

/// <summary>Protects dependency inversion across both domains in the combined host.</summary>
public sealed class ModuleBoundaryTests
{
    [Theory]
    [InlineData("GeoNames.Business")]
    [InlineData("MyVedicStar.Authorize.Business")]
    [InlineData("MyVedicStar.Settings.Business")]
    [InlineData("MyVedicStar.Email.Business")]
    [InlineData("MyVedicDay.Location.Business")]
    [InlineData("MyVedicDay.Localization.Business")]
    [InlineData("MyVedicDay.Traditions.Business")]
    [InlineData("MyVedicDay.Panchang.Business")]
    [InlineData("MyVedicDay.Festivals.Business")]
    [InlineData("MyVedicDay.Muhurta.Business")]
    public void Business_DoesNotReferencePersistenceIntegrationOrComposition(string name)
    {
        var references = Assembly.Load(name).GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference =>
            reference.Name == "GeoNames" ||
            reference.Name!.EndsWith(".Data", StringComparison.Ordinal) ||
            reference.Name.EndsWith(".Integration", StringComparison.Ordinal) ||
            reference.Name.Contains(".Infrastructure.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("MyVedicStar.Shared")]
    [InlineData("MyVedicStar.Authorize.Shared")]
    [InlineData("MyVedicStar.Settings.Shared")]
    [InlineData("MyVedicStar.Email.Shared")]
    public void Shared_DoesNotReferenceImplementationProjects(string name)
    {
        var references = Assembly.Load(name).GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(reference => reference.StartsWith("MyVedic", StringComparison.Ordinal));
        Assert.All(references, reference => Assert.EndsWith(".Shared", reference));
    }
    [Theory]
    [InlineData("GeoNames.Api")]
    [InlineData("MyVedicStar.Authorize.Api")]
    [InlineData("MyVedicStar.Email.Api")]
    [InlineData("MyVedicStar.Settings.Api")]
    [InlineData("MyVedicDay.Location.Api")]
    [InlineData("MyVedicDay.Traditions.Api")]
    [InlineData("MyVedicDay.Panchang.Api")]
    [InlineData("MyVedicDay.Festivals.Api")]
    [InlineData("MyVedicDay.Muhurta.Api")]
    public void ModuleApis_DoNotReferenceImplementationsOrInjectProviders(string name)
    {
        var assembly = Assembly.Load(name);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
            reference.Name == "GeoNames" ||
            reference.Name!.EndsWith(".Data", StringComparison.Ordinal) ||
            reference.Name.EndsWith(".Integration", StringComparison.Ordinal) ||
            reference.Name.EndsWith(".Caching", StringComparison.Ordinal));
        var controllers = assembly.GetTypes().Where(type =>
            typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(type));
        Assert.NotEmpty(controllers);
        foreach (var parameter in controllers.SelectMany(type => type.GetConstructors())
                     .SelectMany(constructor => constructor.GetParameters()))
        {
            Assert.DoesNotContain("Provider", parameter.ParameterType.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("Cache", parameter.ParameterType.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("Repository", parameter.ParameterType.Name, StringComparison.Ordinal);
        }
    }
}

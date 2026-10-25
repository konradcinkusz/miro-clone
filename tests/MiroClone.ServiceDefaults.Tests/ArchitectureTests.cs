using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>
/// The kernel ceiling (P2), checked mechanically: plumbing only, no domain types, no reference to a service.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Kernel = typeof(ServiceDefaultsExtensions).Assembly;

    [Fact]
    public void KernelReferencesNoMiroCloneAssembly()
    {
        var references = Kernel.GetReferencedAssemblies()
            .Select(name => name.Name)
            .Where(name => name is not null && name.StartsWith("MiroClone.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(references);
    }

    [Fact]
    public void KernelDeclaresNoEnum()
    {
        var enums = Kernel.GetExportedTypes().Where(type => type.IsEnum).Select(type => type.FullName).ToList();

        Assert.Empty(enums);
    }

    [Fact]
    public void KernelDeclaresNoEntityOrDbContextType()
    {
        var entityTypes = Kernel.GetExportedTypes()
            .Where(type => typeof(DbContext).IsAssignableFrom(type)
                || type.GetProperties().Any(property => property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(entityTypes);
    }
}

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.Web.Tests;

/// <summary>ADR 0019: the Web app persists its Data Protection key ring to DataProtection:KeysPath when it is configured.</summary>
public sealed class DataProtectionKeysTests
{
    [Fact]
    public void ConfiguredKeysPathReceivesTheKeyRing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"miroclone-keys-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeysPath"] = directory })
                .Build();
            var services = new ServiceCollection();
            services.AddWebApp(configuration);

            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("probe");

            Assert.NotEmpty(Directory.GetFiles(directory, "key-*.xml"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

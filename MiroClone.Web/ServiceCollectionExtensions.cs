using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MiroClone.Web;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Razor components with the interactive Server render mode (ADR 0001). Typed backend clients arrive with WP05.
    /// When DataProtection:KeysPath is set, the key ring persists there, so antiforgery tokens survive a restart (ADR 0019).
    /// </summary>
    public static IServiceCollection AddWebApp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRazorComponents().AddInteractiveServerComponents();

        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
                .SetApplicationName("MiroClone.Web");
        }

        return services;
    }
}

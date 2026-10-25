using Microsoft.Extensions.DependencyInjection;

namespace MiroClone.Web;

public static class ServiceCollectionExtensions
{
    /// <summary>Razor components with the interactive Server render mode (ADR 0001). Typed backend clients arrive with WP05.</summary>
    public static IServiceCollection AddWebApp(this IServiceCollection services)
    {
        services.AddRazorComponents().AddInteractiveServerComponents();
        return services;
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace MiroClone.ServiceDefaults.Tests;

/// <summary>Builds in-process hosts on TestServer. No socket is opened, so no test needs the network.</summary>
internal static class TestApps
{
    public static WebApplicationBuilder NewBuilder(IDictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }
}

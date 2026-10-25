using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace MiroClone.Web.Tests.TestSupport;

/// <summary>A host environment for registering services outside a running host.</summary>
internal sealed class TestEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;

    public string ApplicationName { get; set; } = "MiroClone.Web.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

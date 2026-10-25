using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.CanvasService.Tests;

/// <summary>Canvas validates authservice tokens with the same kernel path as boards; a missing authority stops startup.</summary>
public sealed class AuthorityWiringTests
{
    [Fact]
    public void StartupFailsWithoutAnAuthority()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:Audience"] = "miro-clone" });

        var error = Assert.Throws<InvalidOperationException>(() => builder.Services.AddCanvasService(builder.Configuration));

        Assert.Contains("Auth:Authority", error.Message);
    }
}

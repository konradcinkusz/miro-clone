using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.AppHost.Tests;

/// <summary>The demo seed is a Development-only resource that waits for the services it calls (ADR 0050).</summary>
public sealed class SeedWiringTests
{
    private static readonly string Root = AppContext.BaseDirectory.Split("tests", 2)[0];

    private static DistributedApplicationModel Build(params string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        builder.AddMiroCloneEstate(Root);
        return builder.Build().Services.GetRequiredService<DistributedApplicationModel>();
    }

    private static string[] WaitsFor(IResource resource) =>
        resource.Annotations.OfType<WaitAnnotation>().Select(annotation => annotation.Resource.Name).ToArray();

    [Fact]
    public void InDevelopmentTheSeedIsAProjectThatWaitsForAuthServiceBoardsAndCanvas()
    {
        var seed = Assert.Single(Build("--environment=Development").Resources.OfType<ProjectResource>(), p => p.Name == "seed");

        Assert.Equal(["authservice", "boards-service", "canvas-service"], WaitsFor(seed).Order());
        Assert.True(File.Exists(seed.GetProjectMetadata().ProjectPath));
    }

    [Fact]
    public void TheSwitchTurnsItOffInDevelopment()
    {
        var names = Build("--environment=Development", "--Seed:Enabled=false").Resources.Select(resource => resource.Name);

        Assert.DoesNotContain("seed", names);
    }

    [Fact]
    public void OutsideDevelopmentThereIsNoSeed()
    {
        Assert.DoesNotContain("seed", Build("--environment=Production").Resources.Select(resource => resource.Name));
    }

    [Fact]
    public void AnExplicitSwitchOutsideDevelopmentStopsTheAppHost()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build("--environment=Production", "--Seed:Enabled=true"));

        Assert.Contains("refused outside Development", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoServiceWaitsForTheSeedSoTheAppStartsAtOnce()
    {
        var model = Build("--environment=Development");

        foreach (var resource in model.Resources.Where(resource => resource.Name != "seed"))
        {
            Assert.DoesNotContain("seed", WaitsFor(resource));
        }
    }
}

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
    public async Task EveryProjectRunsInTheEnvironmentOfTheAppHost()
    {
        foreach (var environmentName in new[] { "Development", "Staging" })
        {
            var projects = Build($"--environment={environmentName}").Resources.OfType<ProjectResource>().ToArray();

            Assert.NotEmpty(projects);
            foreach (var project in projects)
            {
                var executionContext = new DistributedApplicationExecutionContext(
                    new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run));
                var context = new EnvironmentCallbackContext(executionContext, resource: project);
                foreach (var callback in project.Annotations.OfType<EnvironmentCallbackAnnotation>())
                {
                    await callback.Callback(context);
                }

                Assert.Equal(environmentName, context.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"]);
                Assert.Equal(environmentName, context.EnvironmentVariables["DOTNET_ENVIRONMENT"]);
            }
        }
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

    [Theory]
    [InlineData("Development", null, "true")]
    [InlineData("Development", "false", "false")]
    [InlineData("Production", null, "false")]
    public async Task TheWebAppIsToldWhetherSeedingIsOnSoItCanShowTheDemoLogins(string environment, string? seedEnabled, string expected)
    {
        var args = seedEnabled is null ? new[] { $"--environment={environment}" } : [$"--environment={environment}", $"--Seed:Enabled={seedEnabled}"];
        var web = Assert.Single(Build(args).Resources.OfType<ProjectResource>(), project => project.Name == "web");
        var executionContext = new DistributedApplicationExecutionContext(
            new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run));
        var context = new EnvironmentCallbackContext(executionContext, resource: web);
        foreach (var callback in web.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        Assert.Equal(expected, context.EnvironmentVariables["Seed__Enabled"]);
    }
}

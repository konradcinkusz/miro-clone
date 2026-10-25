using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.AppHost.Tests;

/// <summary>
/// Pins the estate's shape from its model: builds the graph without starting containers, so no Docker or network is needed.
/// The live run (dotnet run --project MiroClone.AppHost, all resources healthy) is verified separately, with Docker.
/// </summary>
public sealed class EstateWiringTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static DistributedApplicationModel BuildModel()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddMiroCloneEstate(RepositoryRoot);
        var app = builder.Build();
        return app.Services.GetRequiredService<DistributedApplicationModel>();
    }

    [Fact]
    public void EstateDeclaresPostgreSqlWithOneDatabasePerDataService()
    {
        var names = BuildModel().Resources.Select(resource => resource.Name).ToArray();

        Assert.Contains("postgres", names);
        Assert.Contains("boards", names);
        Assert.Contains("canvas", names);
    }

    [Fact]
    public void EveryServiceAndTheWebAppIsDeclaredAsAProjectWhoseFileExists()
    {
        var projects = BuildModel().Resources.OfType<ProjectResource>().ToArray();

        Assert.Equal(["boards-service", "canvas-service", "web"], projects.Select(p => p.Name).Order());
        foreach (var project in projects)
        {
            var path = project.GetProjectMetadata().ProjectPath;
            Assert.True(File.Exists(path), $"{project.Name} points at a project file that does not exist: {path}");
        }
    }

    [Fact]
    public void EachDataServiceWaitsForItsOwnDatabaseAndTheWebAppWaitsForBothServices()
    {
        // Aspire adds the hosting server to a database reference's waits, so a data service waits for its database
        // and for the PostgreSQL server that holds it. No service waits for the other service's database.
        var resources = BuildModel().Resources.ToDictionary(resource => resource.Name);

        Assert.Equal(["boards", "postgres"], WaitsFor(resources["boards-service"]).Order());
        Assert.Equal(["canvas", "postgres"], WaitsFor(resources["canvas-service"]).Order());
        Assert.Equal(["boards-service", "canvas-service"], WaitsFor(resources["web"]).Order());
    }

    [Fact]
    public void BoardsServiceReferencesCanvasForThePurgeWithoutWaitingOnIt()
    {
        var resources = BuildModel().Resources.ToDictionary(resource => resource.Name);

        var references = resources["boards-service"].Annotations.OfType<ResourceRelationshipAnnotation>()
            .Select(annotation => annotation.Resource.Name)
            .ToArray();

        Assert.Contains("canvas-service", references);
        Assert.DoesNotContain("canvas-service", WaitsFor(resources["boards-service"]));
    }

    [Fact]
    public void EveryServiceAndTheWebAppIsGatedOnItsHealthProbe()
    {
        var resources = BuildModel().Resources.ToDictionary(resource => resource.Name);

        foreach (var name in new[] { "boards-service", "canvas-service", "web" })
        {
            Assert.Contains(resources[name].Annotations.OfType<HealthCheckAnnotation>(), _ => true);
        }
    }

    [Fact]
    public void WebAppIsTheOnlyResourceWithExternalEndpoints()
    {
        var resources = BuildModel().Resources.OfType<ProjectResource>().ToArray();

        var exposed = resources
            .Where(resource => resource.Annotations.OfType<EndpointAnnotation>().Any(endpoint => endpoint.IsExternal))
            .Select(resource => resource.Name)
            .ToArray();

        Assert.Equal(["web"], exposed);
    }

    private static string[] WaitsFor(IResource resource) =>
        resource.Annotations.OfType<WaitAnnotation>().Select(annotation => annotation.Resource.Name).ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MiroClone.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("MiroClone.sln was not found above the test output.");
    }
}

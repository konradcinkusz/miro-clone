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
    public void EstateDeclaresPostgreSqlWithOneDatabasePerService()
    {
        var names = BuildModel().Resources.Select(resource => resource.Name).ToArray();

        Assert.Contains("postgres", names);
        Assert.Contains("boards", names);
        Assert.Contains("canvas", names);
        Assert.Contains("authservice", names);
    }

    [Fact]
    public void EveryMiroCloneServiceAndTheWebAppIsDeclaredAsAProjectWhoseFileExists()
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
    public void AuthServiceIsThePinnedReleaseImageNotABuiltProject()
    {
        var authService = BuildModel().Resources.OfType<ContainerResource>().Single(resource => resource.Name == "authservice");

        var image = authService.Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("ghcr.io", image.Registry);
        Assert.Equal("konradcinkusz/authservice", image.Image);
        Assert.Equal("v0.3.4", image.Tag);
    }

    [Fact]
    public void EachDataServiceWaitsForItsOwnDatabaseAndAuthServiceAndTheWebAppWaitsForBothServices()
    {
        // Aspire adds the hosting server to a database reference's waits, so a data service waits for its database
        // and for the PostgreSQL server that holds it. No service waits for another service's database.
        var resources = BuildModel().Resources.ToDictionary(resource => resource.Name);

        Assert.Equal(["authservice", "boards", "postgres"], WaitsFor(resources["boards-service"]).Order());
        Assert.Equal(["authservice", "canvas", "postgres"], WaitsFor(resources["canvas-service"]).Order());
        Assert.Equal(["authservice", "boards-service", "canvas-service"], WaitsFor(resources["web"]).Order());
        Assert.Equal(["authservice-db", "postgres"], WaitsFor(resources["authservice"]).Order());
    }

    [Fact]
    public void EveryServiceAndTheWebAppIsGatedOnItsHealthProbe()
    {
        var resources = BuildModel().Resources.ToDictionary(resource => resource.Name);

        foreach (var name in new[] { "boards-service", "canvas-service", "web", "authservice" })
        {
            Assert.Contains(resources[name].Annotations.OfType<HealthCheckAnnotation>(), _ => true);
        }
    }

    [Fact]
    public async Task ConsumersReadTheAuthorityAndTheEstateAudienceFromAuthService()
    {
        var resources = BuildModel().Resources.ToDictionary(resource => resource.Name);

        foreach (var name in new[] { "boards-service", "canvas-service", "web" })
        {
            var environment = await EnvironmentOf(resources[name]);

            Assert.Equal("MiroClone", environment["Auth__Issuer"]);
            Assert.Equal("miro-clone", environment["Auth__Audience"]);
            Assert.Equal("false", environment["Auth__RequireHttpsMetadata"]);
            Assert.True(ReferencesResource(environment["Auth__Authority"], "authservice"));
        }
    }

    [Fact]
    public async Task AuthServiceSignsWithRs256FromASecretParameterAndNeverWithASharedSecret()
    {
        var authService = BuildModel().Resources.Single(resource => resource.Name == "authservice");

        var environment = await EnvironmentOf(authService);

        Assert.Equal("RS256", environment["Jwt__Algorithm"]);
        Assert.Equal("MiroClone", environment["Jwt__Issuer"]);
        Assert.Equal("miro-clone", environment["Jwt__Audience"]);
        var connection = Assert.IsType<ReferenceExpression>(environment["ConnectionStrings__DefaultConnection"]);
        Assert.True(ReferencesResource(connection, "postgres"));
        Assert.EndsWith(";Database=authservice", connection.Format, StringComparison.Ordinal);
        Assert.False(environment.ContainsKey("Jwt__SecretKey"));
        Assert.True(environment.ContainsKey("Jwt__PrivateKeyPem"));
    }

    [Fact]
    public void WebAppIsTheOnlyResourceWithExternalEndpoints()
    {
        var resources = BuildModel().Resources.ToArray();

        var exposed = resources
            .Where(resource => resource.Annotations.OfType<EndpointAnnotation>().Any(endpoint => endpoint.IsExternal))
            .Select(resource => resource.Name)
            .ToArray();

        Assert.Equal(["web"], exposed);
    }

    private static string[] WaitsFor(IResource resource) =>
        resource.Annotations.OfType<WaitAnnotation>().Select(annotation => annotation.Resource.Name).ToArray();

    /// <summary>Whether a resolved environment value is built from the named resource's address.</summary>
    private static bool ReferencesResource(object? value, string resourceName) =>
        value is ReferenceExpression expression && expression.ValueProviders.Any(provider => provider switch
        {
            IResource resource => resource.Name == resourceName,
            EndpointReferenceExpression endpoint => endpoint.Endpoint.Resource.Name == resourceName,
            _ => false,
        });

    /// <summary>The environment the resource would start with, from its callbacks, without starting anything.</summary>
    private static async Task<Dictionary<string, object>> EnvironmentOf(IResource resource)
    {
        var executionContext = new DistributedApplicationExecutionContext(
            new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Run));
        var context = new EnvironmentCallbackContext(executionContext, resource: resource);

        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }

        return context.EnvironmentVariables.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

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

using Aspire.Hosting;

namespace MiroClone.AppHost;

public static class EstateExtensions
{
    /// <summary>
    /// Declares PostgreSQL with one database per service (ADR 0003), the two data services, and the web app.
    /// Edges: each service references its own database; the web app references both services and waits for them.
    /// Each project gets an "http" endpoint whose port Aspire assigns through ASPNETCORE_HTTP_PORTS (no launch profile is
    /// needed), and every service is gated on its /health, so the dashboard shows healthy only when the probe answers.
    /// </summary>
    /// <param name="repositoryRoot">Directory holding the solution's project folders (the parent of this AppHost).</param>
    public static IDistributedApplicationBuilder AddMiroCloneEstate(
        this IDistributedApplicationBuilder builder,
        string repositoryRoot)
    {
        var postgres = builder.AddPostgres("postgres").WithDataVolume();
        var boardsDatabase = postgres.AddDatabase("boards");
        var canvasDatabase = postgres.AddDatabase("canvas");

        var boardsService = builder
            .AddProject("boards-service", ProjectPath(repositoryRoot, "MiroClone.BoardsService"))
            .WithHttpEndpoint(env: "ASPNETCORE_HTTP_PORTS")
            .WithReference(boardsDatabase)
            .WaitFor(boardsDatabase)
            .WithHttpHealthCheck("/health");

        var canvasService = builder
            .AddProject("canvas-service", ProjectPath(repositoryRoot, "MiroClone.CanvasService"))
            .WithHttpEndpoint(env: "ASPNETCORE_HTTP_PORTS")
            .WithReference(canvasDatabase)
            .WaitFor(canvasDatabase)
            .WithHttpHealthCheck("/health");

        builder
            .AddProject("web", ProjectPath(repositoryRoot, "MiroClone.Web"))
            .WithHttpEndpoint(env: "ASPNETCORE_HTTP_PORTS")
            .WithExternalHttpEndpoints()
            .WithReference(boardsService)
            .WaitFor(boardsService)
            .WithReference(canvasService)
            .WaitFor(canvasService)
            .WithHttpHealthCheck("/health");

        return builder;
    }

    private static string ProjectPath(string repositoryRoot, string projectName) =>
        Path.Combine(repositoryRoot, projectName, $"{projectName}.csproj");
}

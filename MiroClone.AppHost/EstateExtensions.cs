using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace MiroClone.AppHost;

public static class EstateExtensions
{
    /// <summary>The authservice image this estate pins (ADR 0011). Bump the tag only with an ADR-recorded review of its release notes.</summary>
    public const string AuthServiceRegistry = "ghcr.io";
    public const string AuthServiceImage = "konradcinkusz/authservice";
    public const string AuthServiceTag = "v0.3.4";

    /// <summary>Issuer and audience of every token the estate accepts (ADR 0011). One audience: authservice mints one per token.</summary>
    public const string SeedEnabledKey = "Seed:Enabled";

    /// <summary>The authservice container's name under the AppHost. Compose names its own (compose.yaml).</summary>
    public const string AuthServiceContainerName = "miroclone-authservice-apphost";

    public const string TokenIssuer = "MiroClone";
    public const string TokenAudience = "miro-clone";

    /// <summary>
    /// Declares PostgreSQL with one database per service (ADR 0003), authservice as a pinned container with its own database,
    /// the two data services, and the web app. Edges: each service references its own database; the data services and the web
    /// app wait for authservice, which validates their tokens; the web app waits for both services.
    /// Each project gets an "http" endpoint whose port Aspire assigns through ASPNETCORE_HTTP_PORTS (no launch profile is
    /// needed), and every service is gated on its /health, so the dashboard shows healthy only when the probe answers.
    /// </summary>
    /// <param name="repositoryRoot">Directory holding the solution's project folders (the parent of this AppHost).</param>
    public static IDistributedApplicationBuilder AddMiroCloneEstate(
        this IDistributedApplicationBuilder builder,
        string repositoryRoot)
    {
        var postgres = builder.AddPostgres("postgres");
        if (!builder.Configuration.GetValue<bool>(EphemeralDataKey))
        {
            postgres.WithDataVolume();
        }

        var boardsDatabase = postgres.AddDatabase("boards");
        var canvasDatabase = postgres.AddDatabase("canvas");
        var authDatabase = postgres.AddDatabase("authservice-db", "authservice");

        // Development only: the RS256 private key is an Aspire secret parameter. The first run generates a key and Aspire keeps it in
        // the AppHost's user secrets (Parameters:authservice-jwt-private-key), so no step is needed and nothing enters the tree (ADR 0049).
        // A key already in user secrets or the environment is used as it is.
        var jwtPrivateKey = builder.AddParameter("authservice-jwt-private-key", new GeneratedRsaKeyDefault(), secret: true, persist: true);

        var authService = builder
            .AddContainer("authservice", AuthServiceImage, AuthServiceTag)
            .WithImageRegistry(AuthServiceRegistry)
            // A fixed name, so the seed can read the dev outbox (the invitation token) from this container's log.
            .WithContainerName(AuthServiceContainerName)
            .WithHttpEndpoint(port: AuthServicePort(builder), targetPort: 8080, name: "http")
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("ConnectionStrings__DefaultConnection", authDatabase.Resource.ConnectionStringExpression)
            .WithEnvironment("Jwt__Algorithm", "RS256")
            .WithEnvironment("Jwt__PrivateKeyPem", jwtPrivateKey)
            .WithEnvironment("Jwt__Issuer", TokenIssuer)
            .WithEnvironment("Jwt__Audience", TokenAudience)
            .WaitFor(authDatabase)
            .WithHttpHealthCheck("/health/ready");

        // Its own public origin: the jwks_uri in the discovery document must be reachable by the services (docs/DEPLOYMENT.md). The services
        // run on the host here, so this is the host address and the fixed port, never the container-network name that Aspire resolves for a
        // container's reference to itself (the first apphost-e2e run: boards answered 401 to a valid token because it could not fetch the keys).
        authService.WithEnvironment("Jwt__PublicBaseUrl", string.Create(System.Globalization.CultureInfo.InvariantCulture, $"http://localhost:{AuthServicePort(builder)}"));

        var canvasService = builder
            .AddProject("canvas-service", ProjectPath(repositoryRoot, "MiroClone.CanvasService"))
            .WithHttpEndpoint(port: ConfiguredPort(builder, CanvasPortKey), env: "ASPNETCORE_HTTP_PORTS")
            .WithReference(canvasDatabase)
            .WaitFor(canvasDatabase)
            .WithAuthority(authService)
            .WithHttpHealthCheck("/health");

        // Boards reaches Canvas only to purge a deleted board's snapshot. It does not WaitFor Canvas: the purge is best effort,
        // and a wait in both directions would be a startup cycle.
        var boardsService = builder
            .AddProject("boards-service", ProjectPath(repositoryRoot, "MiroClone.BoardsService"))
            .WithHttpEndpoint(port: ConfiguredPort(builder, BoardsPortKey), env: "ASPNETCORE_HTTP_PORTS")
            .WithReference(boardsDatabase)
            .WaitFor(boardsDatabase)
            .WithReference(canvasService)
            .WithAuthority(authService)
            .WithHttpHealthCheck("/health");

        builder
            .AddProject("web", ProjectPath(repositoryRoot, "MiroClone.Web"))
            .WithHttpEndpoint(port: ConfiguredPort(builder, WebPortKey), env: "ASPNETCORE_HTTP_PORTS")
            .WithExternalHttpEndpoints()
            .WithReference(boardsService)
            .WaitFor(boardsService)
            .WithReference(canvasService)
            .WaitFor(canvasService)
            .WithAuthority(authService)
            .WithHttpHealthCheck("/health");

        if (SeedRequested(builder))
        {
            AddSeed(builder, repositoryRoot, authService, boardsService, canvasService);
        }

        return builder;
    }

    /// <summary>
    /// The demo seed (ADR 0050): a one-shot project that waits for authservice, boards-service and canvas-service and then creates the
    /// demo users, organization and boards through their public APIs. It is a resource of Development runs only: with Seed:Enabled
    /// unset it is on in Development and absent elsewhere, and Seed:Enabled=true outside Development stops the AppHost.
    /// The web app does not wait for it: the app starts at once, and the seed finishes a few seconds later.
    /// </summary>
    private static void AddSeed(
        IDistributedApplicationBuilder builder,
        string repositoryRoot,
        IResourceBuilder<ContainerResource> authService,
        IResourceBuilder<ProjectResource> boardsService,
        IResourceBuilder<ProjectResource> canvasService) =>
        builder
            .AddProject("seed", ProjectPath(repositoryRoot, "MiroClone.Seed"))
            .WithEnvironment("Seed__AuthServiceUrl", authService.GetEndpoint("http"))
            .WithEnvironment("Seed__BoardsUrl", boardsService.GetEndpoint("http"))
            .WithEnvironment("Seed__CanvasUrl", canvasService.GetEndpoint("http"))
            .WithEnvironment("Seed__AuthServiceContainer", AuthServiceContainerName)
            .WaitFor(authService)
            .WaitFor(boardsService)
            .WaitFor(canvasService);

    /// <summary>On by default in Development, off elsewhere, and an explicit true outside Development is a startup error.</summary>
    private static bool SeedRequested(IDistributedApplicationBuilder builder)
    {
        var configured = builder.Configuration.GetValue<bool?>(SeedEnabledKey);
        if (!builder.Environment.IsDevelopment())
        {
            return configured is true
                ? throw new InvalidOperationException($"{SeedEnabledKey}=true is refused outside Development ({builder.Environment.EnvironmentName}): the seed creates accounts with published passwords.")
                : false;
        }

        return configured ?? true;
    }

    /// <summary>
    /// Points a service at the authservice in this estate: its authority and the estate's issuer and audience. The authority is
    /// a plain http URL on the developer machine, so https metadata is not required here; deployments keep it required (ADR 0011).
    /// </summary>
    private static IResourceBuilder<T> WithAuthority<T>(this IResourceBuilder<T> service, IResourceBuilder<ContainerResource> authService)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport =>
        service
            .WithEnvironment("Auth__Authority", AuthorityUrl(authService))
            .WithEnvironment("Auth__Issuer", TokenIssuer)
            .WithEnvironment("Auth__Audience", TokenAudience)
            .WithEnvironment("Auth__RequireHttpsMetadata", "false")
            .WaitFor(authService);

    /// <summary>
    /// Configuration keys for the end-to-end run (docs/decisions/0039-e2e-suite-is-playwright-dotnet-and-starts-its-own-estate.md).
    /// Unset, the estate behaves as in development: Aspire assigns the ports and PostgreSQL keeps its data volume.
    /// </summary>
    public const string EphemeralDataKey = "Estate:EphemeralData";
    public const string WebPortKey = "Estate:WebPort";
    public const string AuthServicePortKey = "Estate:AuthServicePort";
    /// <summary>The host port of authservice when none is configured. The same port compose publishes, so the address in this repository's docs holds for both paths.</summary>
    public const int DefaultAuthServicePort = 5080;

    public const string BoardsPortKey = "Estate:BoardsPort";
    public const string CanvasPortKey = "Estate:CanvasPort";

    /// <summary>A fixed host port from configuration, or null so Aspire assigns one.</summary>
    private static int? ConfiguredPort(IDistributedApplicationBuilder builder, string key) =>
        builder.Configuration.GetValue<int?>(key);

    private static int AuthServicePort(IDistributedApplicationBuilder builder) =>
        builder.Configuration.GetValue<int?>(AuthServicePortKey) ?? DefaultAuthServicePort;

    private static ReferenceExpression AuthorityUrl(IResourceBuilder<ContainerResource> authService) =>
        ReferenceExpression.Create($"{authService.GetEndpoint("http").Property(EndpointProperty.Url)}");

    private static string ProjectPath(string repositoryRoot, string projectName) =>
        Path.Combine(repositoryRoot, projectName, $"{projectName}.csproj");
}

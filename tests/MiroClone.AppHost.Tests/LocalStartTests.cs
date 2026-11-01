using System.Security.Cryptography;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace MiroClone.AppHost.Tests;

/// <summary>
/// Regression tests for the local-start failures found when the estate was first run for real (ADR 0049): an AppHost without
/// launch profiles printed nothing, and authservice could not start without a signing key the developer had to create by hand.
/// </summary>
public sealed class LocalStartTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void TheSigningKeyIsAFreshRsaPrivateKeyOfAtLeast2048BitsEachTime()
    {
        var first = new GeneratedRsaKeyDefault().GetDefaultValue();
        var second = new GeneratedRsaKeyDefault().GetDefaultValue();

        Assert.NotEqual(first, second);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(first);
        Assert.True(rsa.KeySize >= 2048);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", first, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSigningKeyIsASecretParameterThatIsPersistedSoTheFirstRunNeedsNoSetup()
    {
        var builder = DistributedApplication.CreateBuilder();
        builder.AddMiroCloneEstate(RepositoryRoot);
        using var app = builder.Build();
        var model = app.Services.GetService(typeof(DistributedApplicationModel)) as DistributedApplicationModel;

        var key = Assert.Single(model!.Resources.OfType<ParameterResource>(), p => p.Name == "authservice-jwt-private-key");
        Assert.True(key.Secret);
    }

    [Fact]
    public void EveryLaunchProfileSetsTheDashboardAndOtlpEndpointsAndTheDashboardUrl()
    {
        var path = Path.Combine(RepositoryRoot, "MiroClone.AppHost", "Properties", "launchSettings.json");
        Assert.True(File.Exists(path), "MiroClone.AppHost/Properties/launchSettings.json is missing: the AppHost starts silently without it.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var profiles = document.RootElement.GetProperty("profiles").EnumerateObject().ToArray();

        Assert.NotEmpty(profiles);
        foreach (var profile in profiles)
        {
            Assert.True(profile.Value.TryGetProperty("applicationUrl", out _), $"{profile.Name}: no dashboard applicationUrl");
            var env = profile.Value.GetProperty("environmentVariables");
            Assert.True(env.TryGetProperty("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", out _), $"{profile.Name}: no OTLP endpoint");
            Assert.True(env.TryGetProperty("ASPIRE_DASHBOARD_RESOURCE_SERVICE_ENDPOINT_URL", out _), $"{profile.Name}: no resource service endpoint");
        }
    }

    [Fact]
    public void TheComposeEstateGivesTheWebAppAWritableKeyDirectoryAndEveryServiceItsAuthority()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot, "compose.yaml"));

        // The image's user cannot write under /app, so the default key directory crashes the web app (found in the first container run).
        Assert.Contains("DataProtection__KeysPath: /data/keys", compose, StringComparison.Ordinal);
        Assert.Contains("Jwt__PrivateKeyPem: ${AUTHSERVICE_JWT_PRIVATE_KEY:?", compose, StringComparison.Ordinal);
        Assert.Contains("Auth__Authority: http://authservice:8080", compose, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWebImagePublishRestoresAfterTheSourcesAreCopiedSoItShipsTheBlazorClient()
    {
        var dockerfile = File.ReadAllLines(Path.Combine(RepositoryRoot, "MiroClone.Web", "Dockerfile"));
        var publish = Assert.Single(dockerfile, line => line.TrimStart().StartsWith("RUN dotnet publish", StringComparison.Ordinal));

        // Found in the first containerized CI run: with --no-restore the image has no _framework/blazor.web.js and no static asset
        // endpoints, answers 404 for the Blazor client, and no circuit starts.
        Assert.DoesNotContain("--no-restore", publish, StringComparison.Ordinal);
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

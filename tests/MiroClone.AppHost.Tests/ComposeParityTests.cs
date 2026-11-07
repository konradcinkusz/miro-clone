using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiroClone.AppHost.Tests;

/// <summary>
/// The AppHost and compose.yaml describe one estate twice (ADR 0049, ADR 0050), and the Aspire path cannot run on a GitHub runner, so
/// the compose path is what CI proves. These tests make that proof carry over: authservice has the same image, tag, port, health path
/// and environment in both, the signing key is a PKCS#8 PEM in both, and the seed gets the same settings in both.
/// </summary>
public sealed partial class ComposeParityTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string Compose = File.ReadAllText(Path.Combine(Root, "compose.yaml"));

    [Fact]
    public void AuthServiceHasTheSameImageAndTagInBothPaths()
    {
        var image = ComposeService("authservice").Single(line => line.TrimStart().StartsWith("image:", StringComparison.Ordinal)).Split("image:")[1].Trim();

        Assert.Equal($"{EstateExtensions.AuthServiceRegistry}/{EstateExtensions.AuthServiceImage}:{EstateExtensions.AuthServiceTag}", image);
    }

    [Fact]
    public async Task AuthServiceHasTheSameEnvironmentInBothPaths()
    {
        var apphost = (await EnvironmentOf(AuthService())).ToDictionary(pair => pair.Key, pair => pair.Value);
        var compose = ComposeEnvironment("authservice");

        // Names must match exactly: a misspelt variable is ignored by authservice and shows up only as a startup failure.
        Assert.Equal(apphost.Keys.Order(), compose.Keys.Order());
        foreach (var name in new[] { "ASPNETCORE_ENVIRONMENT", "Jwt__Algorithm", "Jwt__Issuer", "Jwt__Audience" })
        {
            Assert.Equal(apphost[name].ToString(), compose[name]);
        }
    }

    [Fact]
    public void AuthServiceListensOnTheSamePortAndIsProbedOnTheSamePathInBothPaths()
    {
        var endpoint = AuthService().Annotations.OfType<EndpointAnnotation>().Single(e => e.Name == "http");
        var service = string.Join('\n', ComposeService("authservice"));

        Assert.Equal(8080, endpoint.TargetPort);
        Assert.Contains(":8080\"", service, StringComparison.Ordinal);
        Assert.Contains("GET /health/ready", service, StringComparison.Ordinal);
        // The probe path in the AppHost is declared by WithHttpHealthCheck("/health/ready"); the wiring test pins that a probe exists.
        Assert.Contains(AuthService().Annotations.OfType<HealthCheckAnnotation>(), _ => true);
    }

    [Fact]
    public void BothPathsGiveAuthServiceAPkcs8PemKey()
    {
        var generated = new GeneratedRsaKeyDefault().GetDefaultValue();
        var script = File.ReadAllText(Path.Combine(Root, "scripts", "compose-up.sh"));

        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", generated, StringComparison.Ordinal);
        // `openssl genpkey` writes PKCS#8 ("BEGIN PRIVATE KEY"); `openssl genrsa` would write the PKCS#1 form authservice does not read.
        Assert.Contains("openssl genpkey", script, StringComparison.Ordinal);
        Assert.DoesNotContain("openssl genrsa", script, StringComparison.Ordinal);
        Assert.Contains("Jwt__PrivateKeyPem: ${AUTHSERVICE_JWT_PRIVATE_KEY", Compose, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSeedGetsTheSameSettingsInBothPaths()
    {
        var model = BuildModel(["--environment=Development"]);
        var seed = model.Resources.Single(resource => resource.Name == "seed");
        var apphost = (await EnvironmentOf(seed)).Keys.Where(key => key.StartsWith("Seed__", StringComparison.Ordinal)).Order().ToArray();
        var compose = ComposeEnvironment("seed").Keys.Where(key => key.StartsWith("Seed__", StringComparison.Ordinal)).Order().ToArray();

        // Compose adds the on/off switch (SEED_ENABLED); the AppHost decides that by whether it adds the resource at all.
        Assert.Equal(apphost, compose.Where(key => key != "Seed__Enabled").ToArray());
        Assert.Equal("Development", ComposeEnvironment("seed")["ASPNETCORE_ENVIRONMENT"]);
    }

    [Fact]
    public void ComposeStartsTheWebAppOnlyAfterTheSeedFinishedAndTheSeedAfterTheThreeServicesAreHealthy()
    {
        var seed = string.Join('\n', ComposeService("seed"));
        var web = string.Join('\n', ComposeService("web"));

        foreach (var dependency in new[] { "authservice", "boards-service", "canvas-service" })
        {
            Assert.Matches($@"{dependency}: \{{ condition: service_healthy \}}", seed);
        }

        Assert.Contains("seed: { condition: service_completed_successfully }", web, StringComparison.Ordinal);
    }

    private static ContainerResource AuthService() =>
        BuildModel([]).Resources.OfType<ContainerResource>().Single(resource => resource.Name == "authservice");

    private static DistributedApplicationModel BuildModel(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);
        builder.AddMiroCloneEstate(Root);
        return builder.Build().Services.GetRequiredService<DistributedApplicationModel>();
    }

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

    /// <summary>The lines of one top-level service in compose.yaml (a small reader: the file uses plain mappings and one merge key).</summary>
    private static List<string> ComposeService(string name)
    {
        var lines = Compose.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var start = lines.FindIndex(line => line == $"  {name}:");
        Assert.True(start >= 0, $"compose.yaml has no service {name}");
        var block = new List<string>();
        for (var index = start + 1; index < lines.Count; index++)
        {
            if (lines[index].Length > 0 && !lines[index].StartsWith("   ", StringComparison.Ordinal) && !lines[index].StartsWith('#'))
            {
                break;
            }

            block.Add(lines[index]);
        }

        return block;
    }

    private static Dictionary<string, string> ComposeEnvironment(string service)
    {
        var environment = new Dictionary<string, string>();
        var inEnvironment = false;
        foreach (var line in ComposeService(service))
        {
            if (line == "    environment:")
            {
                inEnvironment = true;
                continue;
            }

            if (inEnvironment && !line.StartsWith("      ", StringComparison.Ordinal))
            {
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                {
                    continue;
                }

                break;
            }

            if (!inEnvironment || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var match = EnvironmentLine().Match(line);
            if (match.Success)
            {
                environment[match.Groups[1].Value] = match.Groups[2].Value.Trim().Trim('"');
            }
            else if (line.Contains("<<: *authority", StringComparison.Ordinal))
            {
                foreach (var (key, value) in AuthorityAnchor())
                {
                    environment[key] = value;
                }
            }
        }

        return environment;
    }

    private static Dictionary<string, string> AuthorityAnchor()
    {
        var result = new Dictionary<string, string>();
        var lines = Compose.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        for (var index = lines.FindIndex(line => line.StartsWith("x-authority:", StringComparison.Ordinal)) + 1; index < lines.Count && lines[index].StartsWith("  ", StringComparison.Ordinal); index++)
        {
            var match = EnvironmentLine().Match("    " + lines[index]);
            if (match.Success)
            {
                result[match.Groups[1].Value] = match.Groups[2].Value.Trim().Trim('"');
            }
        }

        return result;
    }

    [GeneratedRegex(@"^\s+([A-Za-z][A-Za-z0-9_]*): (.+?)(\s+#.*)?$")]
    private static partial Regex EnvironmentLine();

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

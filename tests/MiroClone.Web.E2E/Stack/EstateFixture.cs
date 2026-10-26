using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Playwright;
using Xunit;

namespace MiroClone.Web.E2E.Stack;

/// <summary>
/// The estate under test and one Chromium for every journey (WP12, ADR 0039).
/// <para>
/// With <c>MIROCLONE_E2E_BASE_URL</c> set, the suite uses the estate at that address. Otherwise it starts the AppHost itself, on
/// fixed ports, with an empty PostgreSQL and an RS256 key generated for this run. A missing prerequisite (no Chromium, no Docker,
/// no reachable estate) skips the journeys and says why. With <c>MIROCLONE_E2E_REQUIRED=1</c>, as CI sets it, the same gap fails the
/// run instead, so a skip cannot pass as green.
/// </para>
/// </summary>
public sealed class EstateFixture : IAsyncLifetime
{
    public const string BaseUrlVariable = "MIROCLONE_E2E_BASE_URL";
    public const string AuthServiceUrlVariable = "MIROCLONE_E2E_AUTHSERVICE_URL";
    public const string AuthServiceContainerVariable = "MIROCLONE_E2E_AUTHSERVICE_CONTAINER";
    public const string ChromiumVariable = "MIROCLONE_E2E_CHROMIUM";
    public const string RequiredVariable = "MIROCLONE_E2E_REQUIRED";

    /// <summary>The pinned authservice image (AppHost EstateExtensions). The dev outbox is read from its container log.</summary>
    private const string AuthServiceImage = "ghcr.io/konradcinkusz/authservice:v0.3.4";
    private static readonly TimeSpan StartupLimit = TimeSpan.FromMinutes(12);

    private Process? _apphost;
    private IPlaywright? _playwright;
    private string? _skipReason;

    public IBrowser? Browser { get; private set; }

    /// <summary>The web app's address, for example http://127.0.0.1:5123. Empty when the journeys are skipped.</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>authservice's address, the one the account API calls use.</summary>
    public string AuthServiceUrl { get; private set; } = string.Empty;

    /// <summary>The authservice container whose log is the dev outbox, or null when none is known.</summary>
    public string? AuthServiceContainer { get; private set; }

    /// <summary>Why the journeys do not run, or null when they do.</summary>
    public string? SkipReason => _skipReason;

    public async ValueTask InitializeAsync()
    {
        Assertions.SetDefaultExpectTimeout(15_000);

        try
        {
            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                ExecutablePath = NullIfEmpty(Environment.GetEnvironmentVariable(ChromiumVariable)),
            });
        }
        catch (Exception error)
        {
            Unavailable($"Chromium cannot be launched ({FirstLine(error.Message)}). Install it with 'playwright install chromium', or set {ChromiumVariable}.");
            return;
        }

        var suppliedUrl = Environment.GetEnvironmentVariable(BaseUrlVariable);
        if (!string.IsNullOrWhiteSpace(suppliedUrl))
        {
            BaseUrl = suppliedUrl.TrimEnd('/');
            AuthServiceUrl = (Environment.GetEnvironmentVariable(AuthServiceUrlVariable) ?? string.Empty).TrimEnd('/');
            AuthServiceContainer = Environment.GetEnvironmentVariable(AuthServiceContainerVariable);
            if (!await ReachableAsync(BaseUrl + "/health"))
            {
                Unavailable($"no estate answers at {BaseUrl}.");
                return;
            }
        }
        else
        {
            if (!await DockerAvailableAsync())
            {
                Unavailable($"no Docker daemon, so the estate cannot be started. Start Docker, or set {BaseUrlVariable} to a running estate.");
                return;
            }

            await StartEstateAsync();
            AuthServiceContainer = await FindAuthServiceContainerAsync();
        }

        // The web app waits for authservice in the AppHost, so its health implies both. A supplied estate may lack authservice, and
        // the journeys that need it check that themselves (RequireAuthServiceAsync).
        await WaitUntilHealthyAsync(BaseUrl + "/health");
    }

    /// <summary>Skips the calling journey with the reason, unless the estate and the browser are running.</summary>
    public void RequireRunning()
    {
        if (_skipReason is not null)
        {
            Assert.Skip(_skipReason);
        }
    }

    /// <summary>
    /// Skips, or fails under MIROCLONE_E2E_REQUIRED, when authservice does not answer. The journeys that register, sign in or invite
    /// call this first, so a page-only journey never waits for an identity service it does not use.
    /// </summary>
    public async Task RequireAuthServiceAsync()
    {
        RequireRunning();
        if (await ReachableAsync(AuthServiceUrl + "/health/ready"))
        {
            return;
        }

        const string reason = "authservice does not answer, so the journeys that sign in cannot run.";
        if (Environment.GetEnvironmentVariable(RequiredVariable) == "1")
        {
            throw new InvalidOperationException($"The end-to-end suite cannot run: {reason}");
        }

        Assert.Skip($"End-to-end journeys skipped: {reason}");
    }

    /// <summary>Skips the calling journey when the authservice dev outbox cannot be read.</summary>
    public void RequireDevOutbox()
    {
        RequireRunning();
        if (AuthServiceContainer is null)
        {
            Assert.Skip("The authservice container is not known, so its dev outbox cannot be read. Set " + AuthServiceContainerVariable + ".");
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();

        if (_apphost is not null)
        {
            if (!_apphost.HasExited)
            {
                await StopAsync(_apphost);
            }

            _apphost.Dispose();
        }
    }

    private void Unavailable(string reason)
    {
        if (Environment.GetEnvironmentVariable(RequiredVariable) == "1")
        {
            throw new InvalidOperationException($"The end-to-end suite cannot run: {reason}");
        }

        _skipReason = $"End-to-end journeys skipped: {reason}";
    }

    private async Task StartEstateAsync()
    {
        var webPort = FreePort();
        var authPort = FreePort();
        var project = Path.Combine(RepositoryRoot(), "MiroClone.AppHost", "MiroClone.AppHost.csproj");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif

        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var argument in new[] { "run", "--project", project, "--configuration", configuration })
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["Parameters__authservice-jwt-private-key"] = SigningKeyPem();
        start.Environment["Estate__WebPort"] = webPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["Estate__AuthServicePort"] = authPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment["Estate__EphemeralData"] = "true";

        _apphost = Process.Start(start) ?? throw new InvalidOperationException("The AppHost could not be started.");
        BaseUrl = $"http://127.0.0.1:{webPort}";
        AuthServiceUrl = $"http://127.0.0.1:{authPort}";
    }

    private async Task WaitUntilHealthyAsync(string url)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow + StartupLimit;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_apphost is { HasExited: true })
            {
                throw new InvalidOperationException($"The AppHost exited with code {_apphost.ExitCode} before {url} was healthy.");
            }

            if (await ReachableAsync(url, http))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        throw new TimeoutException($"{url} did not answer 200 within {StartupLimit}.");
    }

    private static async Task<bool> ReachableAsync(string url, HttpClient? client = null)
    {
        using var owned = client is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(5) } : null;
        try
        {
            using var reply = await (client ?? owned!).GetAsync(url);
            return reply.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task<bool> DockerAvailableAsync()
    {
        try
        {
            using var docker = Process.Start(new ProcessStartInfo("docker", ["info", "--format", "{{.ServerVersion}}"])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (docker is null)
            {
                return false;
            }

            await docker.StandardOutput.ReadToEndAsync();
            await docker.WaitForExitAsync();
            return docker.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<string?> FindAuthServiceContainerAsync()
    {
        var output = await RunAsync("docker", "ps", "--filter", $"ancestor={AuthServiceImage}", "--format", "{{.Names}}");
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    /// <summary>
    /// Runs a command and returns its standard output and standard error together. <c>docker logs</c> replays the container's
    /// stderr on its own stream, and the authservice dev outbox may be there.
    /// </summary>
    internal static async Task<string> RunAsync(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return await stdout + await stderr;
    }

    private static async Task StopAsync(Process process)
    {
        // SIGINT is what Ctrl+C sends, so the AppHost stops its containers before it exits.
        using (var interrupt = Process.Start(new ProcessStartInfo("kill", ["-INT", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)]) { UseShellExecute = false }))
        {
            interrupt?.WaitForExit();
        }

        using var patience = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(patience.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private static string SigningKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MiroClone.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("MiroClone.sln was not found above the test output.");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string FirstLine(string text) => text.Split('\n')[0].Trim();
}

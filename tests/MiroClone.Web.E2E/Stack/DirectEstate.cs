using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace MiroClone.Web.E2E.Stack;

/// <summary>
/// The estate started without the AppHost (ADR 0041): PostgreSQL and the pinned authservice as containers, and the boards and
/// canvas services and the web app as processes from their built output. Each one gets the settings the AppHost would give it.
/// This verifies the services and the browser journeys. It does not verify the AppHost's composition (register item R1).
/// </summary>
public sealed class DirectEstate : IAsyncDisposable
{
    private const string PostgresImage = "postgres:18.3";
    private const string AuthServiceImage = "ghcr.io/konradcinkusz/authservice:v0.3.4";
    private const string PostgresPassword = "e2e-postgres-only"; // gitleaks:allow
    private static readonly TimeSpan StartupLimit = TimeSpan.FromMinutes(6);

    private readonly string _id = Guid.NewGuid().ToString("N")[..10];
    private readonly List<Process> _processes = [];
    private readonly List<string> _containers = [];
    private string? _network;

    public string BaseUrl { get; private set; } = string.Empty;

    public string AuthServiceUrl { get; private set; } = string.Empty;

    public string AuthServiceContainer { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        var root = RepositoryRoot();
        // The test assembly sits in bin/<configuration>/net10.0, and the services were built in the same configuration.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;

        var authPort = FreePort();
        var boardsPort = FreePort();
        var canvasPort = FreePort();
        var webPort = FreePort();
        AuthServiceUrl = $"http://127.0.0.1:{authPort}";
        var boardsUrl = $"http://127.0.0.1:{boardsPort}";
        var canvasUrl = $"http://127.0.0.1:{canvasPort}";
        BaseUrl = $"http://127.0.0.1:{webPort}";

        _network = $"miroclone-e2e-{_id}";
        await DockerAsync("network", "create", _network);

        var postgres = $"miroclone-e2e-pg-{_id}";
        _containers.Add(postgres);
        await DockerAsync("run", "-d", "--name", postgres, "--network", _network, "--network-alias", "postgres",
            "-p", "127.0.0.1::5432", "-e", $"POSTGRES_PASSWORD={PostgresPassword}", PostgresImage);
        var postgresPort = await PublishedPortAsync(postgres);
        await WaitForPostgresAsync(postgres);
        foreach (var database in new[] { "boards", "canvas", "authservice" })
        {
            await DockerAsync("exec", postgres, "psql", "-h", "127.0.0.1", "-U", "postgres", "-c", $"CREATE DATABASE {database}");
        }

        // The same settings the AppHost gives authservice (EstateExtensions): development, RS256 with a key for this run, one audience.
        AuthServiceContainer = $"miroclone-e2e-auth-{_id}";
        _containers.Add(AuthServiceContainer);
        await DockerAsync("run", "-d", "--name", AuthServiceContainer, "--network", _network, "-p", $"127.0.0.1:{authPort}:8080",
            "-e", "ASPNETCORE_ENVIRONMENT=Development",
            "-e", $"ConnectionStrings__DefaultConnection=Host=postgres;Port=5432;Username=postgres;Password={PostgresPassword};Database=authservice",
            "-e", "Jwt__Algorithm=RS256",
            "-e", $"Jwt__PrivateKeyPem={SigningKeyPem()}",
            "-e", "Jwt__Issuer=MiroClone",
            "-e", "Jwt__Audience=miro-clone",
            "-e", $"Jwt__PublicBaseUrl={AuthServiceUrl}",
            AuthServiceImage);
        await WaitHealthyAsync(AuthServiceUrl + "/health/ready", owner: null);

        var connection = $"Host=127.0.0.1;Port={postgresPort};Username=postgres;Password={PostgresPassword};Database=";
        var authority = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Auth__Authority"] = AuthServiceUrl,
            ["Auth__Issuer"] = "MiroClone",
            ["Auth__Audience"] = "miro-clone",
            ["Auth__RequireHttpsMetadata"] = "false",
        };

        // The same wiring as the AppHost: each database by name, Boards reaches Canvas for the purge, Canvas reaches Boards for access,
        // and Web reaches both by the names service discovery resolves (services__<name>__http__0).
        var boards = Start(root, configuration, "MiroClone.BoardsService", boardsPort, authority, new Dictionary<string, string>
        {
            ["ConnectionStrings__boards"] = connection + "boards",
            ["Canvas__BaseUrl"] = canvasUrl,
        });
        await WaitHealthyAsync(boardsUrl + "/health", boards);

        var canvas = Start(root, configuration, "MiroClone.CanvasService", canvasPort, authority, new Dictionary<string, string>
        {
            ["ConnectionStrings__canvas"] = connection + "canvas",
            ["Boards__BaseAddress"] = boardsUrl + "/",
        });
        await WaitHealthyAsync(canvasUrl + "/health", canvas);

        var web = Start(root, configuration, "MiroClone.Web", webPort, authority, new Dictionary<string, string>
        {
            ["services__boards-service__http__0"] = boardsUrl,
            ["services__canvas-service__http__0"] = canvasUrl,
        });
        await WaitHealthyAsync(BaseUrl + "/health", web);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var process in _processes)
        {
            if (!process.HasExited)
            {
                await InterruptAsync(process);
            }

            process.Dispose();
        }

        foreach (var container in _containers)
        {
            await DockerQuietlyAsync("rm", "-f", container);
        }

        if (_network is not null)
        {
            await DockerQuietlyAsync("network", "rm", _network);
        }
    }

    private Process Start(string root, string configuration, string project, int port,
        IReadOnlyDictionary<string, string> shared, IReadOnlyDictionary<string, string> own)
    {
        var projectDirectory = Path.Combine(root, project);
        var dll = Path.Combine(projectDirectory, "bin", configuration, "net10.0", $"{project}.dll");
        if (!File.Exists(dll))
        {
            throw new FileNotFoundException($"{project} was not built in {configuration}; the suite needs its output.", dll);
        }

        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = projectDirectory };
        start.ArgumentList.Add(dll);
        foreach (var (key, value) in shared.Concat(own))
        {
            start.Environment[key] = value;
        }

        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        var process = Process.Start(start) ?? throw new InvalidOperationException($"{project} could not be started.");
        _processes.Add(process);
        return process;
    }

    private static async Task WaitHealthyAsync(string url, Process? owner)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow + StartupLimit;
        var last = "no answer";
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (owner is { HasExited: true })
            {
                await EstateFixture.DumpDiagnosticsAsync();
                throw new InvalidOperationException($"A service exited with code {owner.ExitCode} before {url} was healthy.");
            }

            try
            {
                using var reply = await http.GetAsync(url);
                if (reply.IsSuccessStatusCode)
                {
                    return;
                }

                last = $"{(int)reply.StatusCode} {await reply.Content.ReadAsStringAsync()}";
            }
            catch (HttpRequestException error)
            {
                last = error.Message;
            }
            catch (TaskCanceledException)
            {
                last = "timed out";
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        await EstateFixture.DumpDiagnosticsAsync();
        throw new TimeoutException($"{url} did not answer 200 within {StartupLimit}. Last answer: {last}");
    }

    private static async Task WaitForPostgresAsync(string container)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            // Over TCP, so the answer comes from the server that takes connections, not from the temporary server of the first start.
            var (exit, _) = await RunAsync("docker", "exec", container, "psql", "-h", "127.0.0.1", "-U", "postgres", "-tAc", "select 1");
            if (exit == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException("PostgreSQL did not accept TCP connections within 3 minutes.");
    }

    private static async Task<int> PublishedPortAsync(string container)
    {
        var (exit, output) = await RunAsync("docker", "port", container, "5432");
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (exit != 0 || line is null)
        {
            throw new InvalidOperationException($"docker port {container} 5432 gave no mapping: {output}");
        }

        return int.Parse(line[(line.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture);
    }

    private static async Task DockerAsync(params string[] arguments)
    {
        var (exit, output) = await RunAsync("docker", arguments);
        if (exit != 0)
        {
            throw new InvalidOperationException($"docker {string.Join(' ', arguments)} failed with exit {exit}: {output}");
        }
    }

    private static Task<(int Exit, string Output)> DockerQuietlyAsync(params string[] arguments) => RunAsync("docker", arguments);

    private static async Task<(int Exit, string Output)> RunAsync(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} could not be started.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var patience = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await process.WaitForExitAsync(patience.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    private static async Task InterruptAsync(Process process)
    {
        using (var interrupt = Process.Start(new ProcessStartInfo("kill", ["-INT", process.Id.ToString(CultureInfo.InvariantCulture)]) { UseShellExecute = false }))
        {
            interrupt?.WaitForExit();
        }

        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
}

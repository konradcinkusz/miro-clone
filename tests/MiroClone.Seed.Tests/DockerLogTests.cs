using System.Diagnostics;
using Xunit;

namespace MiroClone.Seed.Tests;

/// <summary>Reads a real container's log over the Docker socket, the way the seed reads authservice's dev outbox. Skips without Docker.</summary>
public sealed class DockerLogTests
{
    [Fact]
    public async Task TheInvitationTokenIsReadFromARunningContainersLogOverTheDockerSocket()
    {
        if (!File.Exists("/var/run/docker.sock"))
        {
            Assert.Skip("No Docker socket on this machine.");
        }

        var name = $"seed-log-test-{Guid.NewGuid():N}";
        try
        {
            var started = await RunAsync("run", "-d", "--name", name, "alpine:3.20", "sh", "-c", "echo 'info: Invitation token: old_111'; echo 'info: Invitation token: new_222'; sleep 30");
            if (started != 0)
            {
                Assert.Skip("The test image cannot be run here (no registry access).");
            }

            var settings = SeedSettings.From(
                new Dictionary<string, string?>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["Seed__AuthServiceUrl"] = "http://a",
                    ["Seed__BoardsUrl"] = "http://b",
                    ["Seed__CanvasUrl"] = "http://c",
                    ["Seed__AuthServiceContainer"] = name,
                },
                []);

            Assert.Equal("new_222", await new ConfiguredInvitationSource(settings).NewestAsync(CancellationToken.None));
        }
        finally
        {
            await RunAsync("rm", "-f", name);
        }
    }

    private static async Task<int> RunAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}

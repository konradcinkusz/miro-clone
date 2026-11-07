using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace MiroClone.Seed;

/// <summary>
/// Where the seed finds the member's invitation token. authservice returns none: in Development its dev outbox writes the token to the
/// container's console (ADR 0011). The seed reads it, in this order: an explicit token, a log file, the container's log over the Docker
/// socket (the same place a developer reads it in the Aspire dashboard).
/// </summary>
internal interface IInvitationSource
{
    Task<string?> NewestAsync(CancellationToken cancellationToken);
}

internal static partial class InvitationLog
{
    [GeneratedRegex("Invitation token: ([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>The newest token in a log text, or null. A token is as it was printed: no frame bytes of the Docker stream join two lines.</summary>
    public static string? Newest(string log) =>
        Pattern().Matches(log).LastOrDefault()?.Groups[1].Value;
}

internal sealed class ConfiguredInvitationSource(SeedSettings settings, string dockerSocket = "/var/run/docker.sock") : IInvitationSource
{
    public async Task<string?> NewestAsync(CancellationToken cancellationToken)
    {
        if (settings.InvitationToken is { } explicitToken)
        {
            return explicitToken;
        }

        if (settings.AuthServiceLogFile is { } file && File.Exists(file))
        {
            return InvitationLog.Newest(await File.ReadAllTextAsync(file, cancellationToken));
        }

        if (settings.AuthServiceContainer is { } container && File.Exists(dockerSocket))
        {
            return InvitationLog.Newest(await ContainerLogAsync(container, cancellationToken));
        }

        return null;
    }

    /// <summary>GET /containers/{name}/logs on the Docker Engine API. Frames carry an 8-byte header, which the pattern skips over.</summary>
    private async Task<string> ContainerLogAsync(string container, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(dockerSocket), token);
                return new NetworkStream(socket, ownsSocket: true);
            },
        };
        using var docker = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://docker/") };
        using var reply = await docker.GetAsync($"containers/{Uri.EscapeDataString(container)}/logs?stdout=1&stderr=1", cancellationToken);
        if (!reply.IsSuccessStatusCode)
        {
            throw new SeedException($"Docker refused to read the log of container '{container}': {(int)reply.StatusCode}.");
        }

        return await reply.Content.ReadAsStringAsync(cancellationToken);
    }
}

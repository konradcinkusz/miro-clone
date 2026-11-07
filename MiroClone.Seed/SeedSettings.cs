namespace MiroClone.Seed;

/// <summary>
/// What the seed is told by its environment. The seed runs only in Development and only when enabled, and it never invents an
/// address: a missing value is an error that names the variable.
/// </summary>
internal sealed record SeedSettings(
    bool Enabled,
    string Environment,
    Uri AuthServiceUrl,
    Uri BoardsUrl,
    Uri CanvasUrl,
    string? AuthServiceContainer,
    string? InvitationToken,
    string? AuthServiceLogFile,
    bool Reset)
{
    public const string DevelopmentEnvironment = "Development";

    public static SeedSettings From(IReadOnlyDictionary<string, string?> variables, IReadOnlyList<string> arguments)
    {
        string? Get(string name) => variables.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        Uri Url(string name) => Get(name) is { } text && Uri.TryCreate(text.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            ? uri
            : throw new SeedException($"{name} must be an absolute URL (for example http://localhost:5080).");

        var environment = Get("ASPNETCORE_ENVIRONMENT") ?? Get("DOTNET_ENVIRONMENT") ?? "Production";
        var enabled = Get("Seed__Enabled") is not { } flag || !flag.Equals("false", StringComparison.OrdinalIgnoreCase);

        // Refuse before anything else is read: outside Development the seed must not even look at the estate.
        if (enabled && !environment.Equals(DevelopmentEnvironment, StringComparison.Ordinal))
        {
            throw new SeedException($"The seed runs only in {DevelopmentEnvironment}, and this is {environment}. It creates accounts with published passwords.");
        }

        if (!enabled)
        {
            var unused = new Uri("http://disabled.invalid/");
            return new SeedSettings(false, environment, unused, unused, unused, null, null, null, false);
        }

        return new SeedSettings(
            true,
            environment,
            Url("Seed__AuthServiceUrl"),
            Url("Seed__BoardsUrl"),
            Url("Seed__CanvasUrl"),
            Get("Seed__AuthServiceContainer"),
            Get("Seed__InvitationToken"),
            Get("Seed__AuthServiceLogFile"),
            arguments.Contains("--reset", StringComparer.Ordinal));
    }
}

/// <summary>A failure the operator can act on: the message says what to change.</summary>
internal sealed class SeedException(string message) : Exception(message);

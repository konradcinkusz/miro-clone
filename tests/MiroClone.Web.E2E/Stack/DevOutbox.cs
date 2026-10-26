namespace MiroClone.Web.E2E.Stack;

/// <summary>
/// authservice's dev outbox: in Development it logs the invitation and verification tokens instead of mailing them (ADR 0011).
/// The journeys read those tokens from the authservice container's log, as a developer does in the Aspire dashboard.
/// </summary>
public static class DevOutbox
{
    public static async Task<int> InvitationCountAsync(EstateFixture stack)
    {
        stack.RequireDevOutbox();
        return AuthorityApi.InvitationTokens(await LogAsync(stack)).Count;
    }

    /// <summary>Waits until the outbox holds an invitation after the first <paramref name="before"/>, and returns the newest one.</summary>
    public static async Task<string> NewestInvitationAsync(EstateFixture stack, int before)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var tokens = AuthorityApi.InvitationTokens(await LogAsync(stack));
            if (tokens.Count > before)
            {
                return tokens[^1];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new TimeoutException("The invitation did not reach the authservice dev outbox within 30 seconds.");
    }

    private static Task<string> LogAsync(EstateFixture stack) =>
        EstateFixture.RunAsync("docker", "logs", stack.AuthServiceContainer!);
}

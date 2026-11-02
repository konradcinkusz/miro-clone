using Xunit;

namespace MiroClone.Seed.Tests;

public sealed class SeedSettingsTests
{
    private static Dictionary<string, string?> Variables(string environment, string? enabled = null)
    {
        var variables = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = environment,
            ["Seed__AuthServiceUrl"] = "http://localhost:5080",
            ["Seed__BoardsUrl"] = "http://localhost:5081",
            ["Seed__CanvasUrl"] = "http://localhost:5082",
        };
        if (enabled is not null)
        {
            variables["Seed__Enabled"] = enabled;
        }

        return variables;
    }

    [Fact]
    public void ItRefusesOutsideDevelopmentWhateverTheSwitchSays()
    {
        var error = Assert.Throws<SeedException>(() => SeedSettings.From(Variables("Production"), []));
        Assert.Contains("only in Development", error.Message, StringComparison.Ordinal);
        Assert.Throws<SeedException>(() => SeedSettings.From(Variables("Staging", "true"), []));
    }

    [Fact]
    public void ItIsDisabledByTheSwitchAndDoesNotNeedAddresses()
    {
        var settings = SeedSettings.From(new Dictionary<string, string?> { ["ASPNETCORE_ENVIRONMENT"] = "Development", ["Seed__Enabled"] = "false" }, []);

        Assert.False(settings.Enabled);
    }

    [Fact]
    public void ItIsEnabledByDefaultInDevelopmentAndReadsTheAddressesAndReset()
    {
        var settings = SeedSettings.From(Variables("Development"), ["--reset"]);

        Assert.True(settings.Enabled);
        Assert.True(settings.Reset);
        Assert.Equal("http://localhost:5080/", settings.AuthServiceUrl.ToString());
    }

    [Fact]
    public void AMissingAddressNamesTheVariable()
    {
        var variables = Variables("Development");
        variables.Remove("Seed__BoardsUrl");

        var error = Assert.Throws<SeedException>(() => SeedSettings.From(variables, []));

        Assert.Contains("Seed__BoardsUrl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewestInvitationTokenIsReadFromALogWithDockerFrameBytesInIt()
    {
        var log = "\u0001\0\0\0\0\0\00info: Invitation token: first-AAA_1\n\u0001\0\0\0\0\0\00info: Invitation token: second-BBB_2\n";

        Assert.Equal("second-BBB_2", InvitationLog.Newest(log));
        Assert.Null(InvitationLog.Newest("nothing here"));
    }
}

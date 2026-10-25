namespace MiroClone.Web.Tests.TestSupport;

/// <summary>A clock the test moves by hand, so expiry and refresh windows are exact.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}

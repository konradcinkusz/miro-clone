namespace MiroClone.CanvasService.Persistence;

/// <summary>Configuration under the "Canvas" section (P5). The defaults suit a laptop run; deployments override them.</summary>
public sealed class CanvasOptions
{
    public const string SectionName = "Canvas";

    /// <summary>How often dirty boards are flushed. Shorter loses less on a crash; longer writes less (state-snapshot §1).</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Most recently touched boards loaded into memory at startup. Bounded so startup does not grow with history.</summary>
    public int RehydrateLimit { get; set; } = 1000;
}

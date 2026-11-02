namespace Vera.Core;

public enum Verdict
{
    /// <summary>The expected result was seen.</summary>
    Passed,

    /// <summary>The expected result was not seen: an app bug.</summary>
    Failed,

    /// <summary>VERA could not decide, e.g. it hit the step limit or the model kept giving invalid answers.</summary>
    Inconclusive,

    /// <summary>The failure did not happen again in any replay without the AI.</summary>
    NotReproduced,

    /// <summary>The failure happened again in some replays, but not all.</summary>
    Flaky,
}
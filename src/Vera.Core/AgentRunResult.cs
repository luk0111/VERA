namespace Vera.Core;

/// <summary>The outcome of one run of a test.</summary>
/// <param name="Reason">Why VERA reached this verdict, in plain language for the tester.</param>
public sealed record AgentRunResult(TestTask Test, Verdict Verdict, string Reason, IReadOnlyList<AgentStep> Steps);
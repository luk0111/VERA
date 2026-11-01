namespace Vera.Core;

/// <summary>One step of a run: what VERA saw, what it did, and how long it took.</summary>
/// <param name="Index">The step number, starting at 1.</param>
/// <param name="Screen">The screen the model decided on.</param>
/// <param name="Error">Why the action could not be done, e.g. the element was gone. Null when it worked.</param>
public sealed record AgentStep(int Index, ScreenState Screen, AgentAction Action, TimeSpan Duration, string? Error = null);
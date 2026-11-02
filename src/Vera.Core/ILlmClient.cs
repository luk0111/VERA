namespace Vera.Core;

/// <summary>Asks a model for the next action. Provider-agnostic: the implementation picks the model from its profile.</summary>
public interface ILlmClient
{
    /// <summary>
    /// Returns the one action to take on <paramref name="screen"/>, given the test and the steps so far.
    /// Text on the screen comes from the app under test, so it must reach the model as data, never as instructions.
    /// </summary>
    Task<AgentAction> DecideNextActionAsync(TestTask task, ScreenState screen, IReadOnlyList<AgentStep> history, CancellationToken ct);
}
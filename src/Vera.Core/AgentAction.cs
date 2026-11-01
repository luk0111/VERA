namespace Vera.Core;

public enum AgentActionType
{
    /// <summary>Click the element <see cref="AgentAction.TargetId"/>.</summary>
    Click,

    /// <summary>Type <see cref="AgentAction.Text"/> into the element <see cref="AgentAction.TargetId"/>. May contain <c>{{secret:name}}</c>.</summary>
    Type,

    /// <summary>Press the keys in <see cref="AgentAction.Text"/>, e.g. <c>Enter</c> or <c>Ctrl+S</c>.</summary>
    PressKey,

    /// <summary>Wait for the app, e.g. while it loads.</summary>
    Wait,

    /// <summary>The test is over and the expected result was seen: the verdict is Passed.</summary>
    Done,

    /// <summary>The test is over and the expected result was not seen: the verdict is Failed.</summary>
    Fail,
}

/// <summary>The one action the model chose for this step.</summary>
/// <param name="TargetId">The <see cref="UiElement.Id"/> to act on, for <see cref="AgentActionType.Click"/> and <see cref="AgentActionType.Type"/>.</param>
/// <param name="Text">The text to type, or the keys to press.</param>
/// <param name="Reason">Why the model chose it, in one sentence. Shown to the tester and logged.</param>
public sealed record AgentAction(AgentActionType Type, int? TargetId, string? Text, string Reason);
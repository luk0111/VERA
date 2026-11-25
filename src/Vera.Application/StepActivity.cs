namespace Vera.Application;

/// <summary>What VERA is doing in a step right now (#59).</summary>
public enum StepPhase
{
    /// <summary>Reading the window before the step.</summary>
    Reading,

    /// <summary>Asking the model to find the step's element.</summary>
    Finding,

    /// <summary>Reading the window again: the element isn't there or ready yet, or the expected result doesn't hold yet.</summary>
    Waiting,

    /// <summary>Clicking, typing, pressing keys, scrolling, dragging or hovering in the app.</summary>
    Acting,
}

/// <summary>A step that's running and what VERA is doing in it, so the tester can follow along.</summary>
/// <param name="Number">The step's number, from 1.</param>
/// <param name="Step">The step as written.</param>
public sealed record StepActivity(int Number, StepCommand Step, StepPhase Phase);

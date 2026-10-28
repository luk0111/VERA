namespace Vera.Input.Testing;

/// <summary>One call made on a <see cref="FakeScreen"/>, with its arguments.</summary>
public abstract record RecordedAction;

public sealed record Screenshot(double? Scale) : RecordedAction;

public sealed record Moved(int X, int Y) : RecordedAction;

public sealed record Clicked(int X, int Y, MouseButton Button, int Clicks) : RecordedAction;

public sealed record Scrolled(int X, int Y, int DeltaY, int DeltaX) : RecordedAction;

public sealed record Dragged(int FromX, int FromY, int ToX, int ToY, MouseButton Button) : RecordedAction;

public sealed record TypedText(string Text, TypeTextOptions? Options) : RecordedAction;

public sealed record KeyPressed(string Combo, int Repeat) : RecordedAction;
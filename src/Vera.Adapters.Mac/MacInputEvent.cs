using Vera.Input;

namespace Vera.Adapters.Mac;

/// <summary>One input event in VERA's own terms. Positions are global screen points, the unit macOS places windows in.</summary>
internal abstract record MacInputEvent
{
    public sealed record MouseMove(double X, double Y) : MacInputEvent;

    /// <param name="ClickState">1 for a single click, 2 for the second click of a double click, and so on. macOS needs it to see a double click.</param>
    public sealed record MouseButtonEvent(double X, double Y, MouseButton Button, bool Down, int ClickState) : MacInputEvent;

    /// <param name="Flags">The modifiers held while this key event happens (Command, Shift, Option).</param>
    public sealed record Key(ushort KeyCode, bool Down, ulong Flags) : MacInputEvent;

    /// <summary>One character, sent as text so it arrives as written whatever the keyboard layout (ă, ș, €, emoji).</summary>
    public sealed record Text(string Character, bool Down) : MacInputEvent;
}

/// <summary>Sends input events. The only real one is <see cref="CoreGraphicsInputSink"/>; tests record them instead.</summary>
internal interface IMacInputSink
{
    /// <exception cref="InputRejectedException">macOS could not make the event.</exception>
    void Send(MacInputEvent e);
}

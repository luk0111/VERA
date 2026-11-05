namespace Vera.Input.Injection;

/// <summary>Sends input events to Windows. The only place that injects real input.</summary>
internal interface IInputSink
{
    /// <summary>Sends the events in order, as one batch that other input can't interleave with.</summary>
    /// <exception cref="InputRejectedException">Windows injected fewer events than were sent.</exception>
    void Send(params IReadOnlyList<InputEvent> events);
}

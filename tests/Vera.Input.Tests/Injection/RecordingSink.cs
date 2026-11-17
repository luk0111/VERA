using Vera.Input.Injection;

namespace Vera.Input.Tests.Injection;

/// <summary>An <see cref="IInputSink"/> that keeps the events instead of sending them.</summary>
internal sealed class RecordingSink : IInputSink
{
    /// <summary>Every event, in order.</summary>
    public List<InputEvent> Events { get; } = [];

    /// <summary>Each <see cref="Send"/> call, so tests can check what went to Windows together.</summary>
    public List<IReadOnlyList<InputEvent>> Batches { get; } = [];

    public void Send(params IReadOnlyList<InputEvent> events)
    {
        Events.AddRange(events);
        Batches.Add([.. events]);
    }
}

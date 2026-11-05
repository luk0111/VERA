using Vera.Input.Injection;

namespace Vera.Input.Tests.Injection;

/// <summary>An <see cref="IInputSink"/> that keeps the events instead of sending them.</summary>
internal sealed class RecordingSink : IInputSink
{
    public List<InputEvent> Events { get; } = [];

    public void Send(params IReadOnlyList<InputEvent> events) => Events.AddRange(events);
}

namespace Vera.Llm;

public enum ConnectionStatusKind
{
    Connected,
    Unreachable,
    ModelMissing,
}

/// <summary>The result of a connection check: the state plus a message the tester can act on.</summary>
public sealed record ConnectionStatus(ConnectionStatusKind Kind, string Message);
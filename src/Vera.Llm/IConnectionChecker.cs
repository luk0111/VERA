namespace Vera.Llm;

/// <summary>Checks that a profile's provider is reachable and has the model. One implementation per provider.</summary>
public interface IConnectionChecker
{
    /// <summary>Never throws for "not reachable"; only a cancelled <paramref name="ct"/> throws.</summary>
    Task<ConnectionStatus> CheckAsync(LlmProfile profile, CancellationToken ct);
}
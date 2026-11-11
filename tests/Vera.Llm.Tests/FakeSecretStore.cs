using Vera.Core;

namespace Vera.Llm.Tests;

/// <summary>An in-memory secret store. Values here are made up for tests, never real keys.</summary>
internal sealed class FakeSecretStore(params (string Name, string Value)[] secrets) : ISecretStore
{
    private readonly Dictionary<string, string> _values = secrets.ToDictionary(s => s.Name, s => s.Value);

    public Task<string?> GetAsync(string name, CancellationToken ct) =>
        Task.FromResult(_values.TryGetValue(name, out var value) ? value : null);

    public Task SetAsync(string name, string value, CancellationToken ct)
    {
        _values[name] = value;
        return Task.CompletedTask;
    }
}

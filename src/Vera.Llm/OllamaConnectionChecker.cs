using System.Text.Json;

namespace Vera.Llm;

/// <summary>Checks Ollama with a plain <c>GET /api/tags</c>, which lists the installed models.</summary>
public sealed class OllamaConnectionChecker(HttpClient http) : IConnectionChecker
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(2);

    public async Task<ConnectionStatus> CheckAsync(LlmProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);

        IReadOnlyList<string> installed;
        try
        {
            using var response = await http.GetAsync(new Uri("api/tags", UriKind.Relative), timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            installed = ReadModelNames(json);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unreachable();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException)
        {
            return Unreachable();
        }

        var wanted = Normalize(profile.Model);
        return installed.Any(name => Normalize(name) == wanted)
            ? new ConnectionStatus(ConnectionStatusKind.Connected, "")
            : new ConnectionStatus(ConnectionStatusKind.ModelMissing,
                $"Model '{profile.Model}' is not installed. Run: ollama pull {profile.Model}");
    }

    private static ConnectionStatus Unreachable() =>
        new(ConnectionStatusKind.Unreachable, "Ollama is not running. Start it, then try again.");

    // Ollama lists each model with both "name" and "model"; either may be the tag we want.
    private static List<string> ReadModelNames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var names = new List<string>();
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (var model in models.EnumerateArray())
        {
            foreach (var key in (string[])["name", "model"])
            {
                if (model.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    names.Add(value.GetString()!);
                }
            }
        }

        return names;
    }

    // A bare name means the "latest" tag: "qwen3-vl" == "qwen3-vl:latest".
    private static string Normalize(string model)
    {
        var trimmed = model.Trim().ToLowerInvariant();
        return trimmed.Contains(':', StringComparison.Ordinal) ? trimmed : trimmed + ":latest";
    }
}
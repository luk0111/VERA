using System.Net;

using Vera.Core;

namespace Vera.Llm;

/// <summary>Checks Gemini with <c>GET v1beta/models/{model}</c>: is there a key, does Google accept it, and can it use the model.</summary>
public sealed class GeminiConnectionChecker(HttpClient http, ISecretStore secrets) : IConnectionChecker
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    public async Task<ConnectionStatus> CheckAsync(LlmProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var key = await secrets.GetAsync(profile.ApiKeyName, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(key))
        {
            return new ConnectionStatus(ConnectionStatusKind.Unauthorized, GeminiApiKeyHandler.MissingKeyMessage);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(CheckTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"v1beta/models/{Uri.EscapeDataString(profile.Model)}", UriKind.Relative));
            request.Headers.Add("x-goog-api-key", key);
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => new ConnectionStatus(ConnectionStatusKind.Connected, ""),
                HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new ConnectionStatus(ConnectionStatusKind.Unauthorized, "Gemini rejected the API key. Connect a valid key in Settings."),
                HttpStatusCode.NotFound =>
                    new ConnectionStatus(ConnectionStatusKind.ModelMissing, $"Model '{profile.Model}' is not available for this key. Check the model name in appsettings.json."),
                _ => Unreachable(),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unreachable();
        }
        catch (HttpRequestException)
        {
            return Unreachable();
        }
    }

    private static ConnectionStatus Unreachable() =>
        new(ConnectionStatusKind.Unreachable, "Could not reach Gemini. Check your internet connection, then try again.");
}

using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Vera.Llm;

/// <summary>
/// Turns Gemini's error replies into provider-neutral exceptions, so no SDK type reaches the callers:
/// a rejected key → <see cref="LlmUnauthorizedException"/>, the daily quota → <see cref="LlmDailyQuotaException"/>,
/// a per-minute limit → <see cref="LlmRateLimitException"/> (retried by <see cref="RateLimitChatClient"/>). Other replies pass through.
/// </summary>
internal sealed class GeminiErrorHandler : DelegatingHandler
{
    internal const string RejectedKeyMessage = "Gemini rejected the API key. Connect a valid key in Settings → Connect your API key.";

    internal const string DailyQuotaMessage =
        "The free daily quota for this Gemini model is used up. It resets at midnight Pacific time.";

    internal const string RateLimitMessage = "Gemini is getting too many requests per minute. Wait a minute and try again.";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.BadRequest))
        {
            return response;
        }

        // Buffered, so the client can still read the body when the reply passes through.
        await response.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var error = Classify(response.StatusCode, body, response.Headers.RetryAfter?.Delta);
        if (error is null)
        {
            return response;
        }

        response.Dispose();
        throw error;
    }

    /// <summary>The exception for an error reply, or null when the reply should pass through unchanged.</summary>
    internal static Exception? Classify(HttpStatusCode status, string body, TimeSpan? retryAfterHeader)
    {
        switch (status)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                return new LlmUnauthorizedException(RejectedKeyMessage);

            // Gemini answers a wrong key with 400 INVALID_ARGUMENT and the reason API_KEY_INVALID.
            case HttpStatusCode.BadRequest:
                return body.Contains("API_KEY_INVALID", StringComparison.Ordinal) || body.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)
                    ? new LlmUnauthorizedException(RejectedKeyMessage)
                    : null;

            case HttpStatusCode.TooManyRequests:
                var (perDay, retryDelay) = ReadQuotaDetails(body);
                return perDay
                    ? new LlmDailyQuotaException(DailyQuotaMessage)
                    : new LlmRateLimitException(RateLimitMessage, retryDelay ?? retryAfterHeader);

            default:
                return null;
        }
    }

    // Google's error body is {"error":{...,"details":[...]}}; the OpenAI-compatible endpoint sometimes wraps it in an array.
    // QuotaFailure details name the quota (e.g. "GenerateRequestsPerDayPerProjectPerModel-FreeTier"); RetryInfo has "retryDelay": "37s".
    private static (bool PerDay, TimeSpan? RetryDelay) ReadQuotaDetails(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                {
                    return (false, null);
                }

                root = root[0];
            }

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("details", out var details) || details.ValueKind != JsonValueKind.Array)
            {
                return (false, null);
            }

            var perDay = false;
            TimeSpan? retryDelay = null;
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (detail.TryGetProperty("violations", out var violations) && violations.ValueKind == JsonValueKind.Array)
                {
                    perDay |= violations.EnumerateArray().Any(IsPerDay);
                }

                if (detail.TryGetProperty("retryDelay", out var delay) && delay.ValueKind == JsonValueKind.String)
                {
                    retryDelay = ParseSeconds(delay.GetString()!);
                }
            }

            return (perDay, retryDelay);
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }

    private static bool IsPerDay(JsonElement violation) =>
        violation.ValueKind == JsonValueKind.Object
        && violation.TryGetProperty("quotaId", out var id)
        && id.ValueKind == JsonValueKind.String
        && id.GetString()!.Contains("PerDay", StringComparison.OrdinalIgnoreCase);

    // "37s" or "37.5s" (a protobuf Duration).
    private static TimeSpan? ParseSeconds(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.EndsWith('s'))
        {
            trimmed = trimmed[..^1];
        }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }
}

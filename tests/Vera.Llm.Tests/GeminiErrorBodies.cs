namespace Vera.Llm.Tests;

/// <summary>Error bodies in the shape Google's Gemini API sends them (the OpenAI-compatible endpoint wraps them in an array).</summary>
internal static class GeminiErrorBodies
{
    public const string PerMinute = """
        [{"error":{"code":429,"message":"You exceeded your current quota, please check your plan and billing details.","status":"RESOURCE_EXHAUSTED",
        "details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaMetric":"generativelanguage.googleapis.com/generate_content_free_tier_requests",
        "quotaId":"GenerateRequestsPerMinutePerProjectPerModel-FreeTier","quotaDimensions":{"location":"global","model":"gemini-3.5-flash-lite"},"quotaValue":"15"}]},
        {"@type":"type.googleapis.com/google.rpc.Help","links":[{"description":"Learn more about Gemini API quotas","url":"https://ai.google.dev/gemini-api/docs/rate-limits"}]},
        {"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"37s"}]}}]
        """;

    public const string PerDay = """
        {"error":{"code":429,"message":"You exceeded your current quota, please check your plan and billing details.","status":"RESOURCE_EXHAUSTED",
        "details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaMetric":"generativelanguage.googleapis.com/generate_content_free_tier_requests",
        "quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier","quotaDimensions":{"location":"global","model":"gemini-3.5-flash-lite"},"quotaValue":"500"}]},
        {"@type":"type.googleapis.com/google.rpc.RetryInfo","retryDelay":"12s"}]}}
        """;

    public const string InvalidKey = """
        [{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT",
        "details":[{"@type":"type.googleapis.com/google.rpc.ErrorInfo","reason":"API_KEY_INVALID","domain":"googleapis.com"}]}}]
        """;
}

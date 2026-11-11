using System.Text;

using Microsoft.Extensions.AI;

using Vera.Core;

namespace Vera.Llm.Tests;

/// <summary>
/// Talks to the real Gemini API with the key in <c>VERA_GEMINI_API_KEY</c>; skipped when it is not set. CI skips it.
/// Uses one request of the free daily quota per run.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GeminiIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_short_question_streams_a_reply_with_usage_and_little_thinking()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentSecretStore.VariableName("gemini"))))
        {
            Assert.Skip("Set VERA_GEMINI_API_KEY to run this test.");
        }

        var profile = new LlmProfile
        {
            Name = "gemini",
            Provider = "Gemini",
            Endpoint = "https://generativelanguage.googleapis.com",
            Model = "gemini-3.5-flash-lite",
            ApiKeyName = "gemini",
            Think = false,
        };
        using var client = GeminiChatClientFactory.Create(profile, new EnvironmentSecretStore());

        var text = new StringBuilder();
        UsageDetails? usage = null;
        await foreach (var update in client.GetStreamingResponseAsync("Reply with exactly one word: ready", cancellationToken: Ct))
        {
            text.Append(update.Text);
            usage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details ?? usage;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Reply: {text}; input tokens: {usage?.InputTokenCount}; output tokens: {usage?.OutputTokenCount}; total: {usage?.TotalTokenCount}");

        text.ToString().ShouldNotBeNullOrWhiteSpace();
        usage.ShouldNotBeNull();
        usage.OutputTokenCount.ShouldNotBeNull();
        usage.OutputTokenCount.Value.ShouldBeLessThan(1000, "Low reasoning effort should keep hidden thinking small.");
    }
}

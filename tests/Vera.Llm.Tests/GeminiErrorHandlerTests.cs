using System.Net;

namespace Vera.Llm.Tests;

public sealed class GeminiErrorHandlerTests
{
    [Fact]
    public void A_per_minute_limit_is_retryable_with_the_delay_Google_asked_for()
    {
        var error = GeminiErrorHandler.Classify(HttpStatusCode.TooManyRequests, GeminiErrorBodies.PerMinute, null);

        var rateLimit = error.ShouldBeOfType<LlmRateLimitException>();
        rateLimit.RetryAfter.ShouldBe(TimeSpan.FromSeconds(37));
    }

    [Fact]
    public void The_daily_quota_is_its_own_error()
    {
        var error = GeminiErrorHandler.Classify(HttpStatusCode.TooManyRequests, GeminiErrorBodies.PerDay, null);

        error.ShouldBeOfType<LlmDailyQuotaException>().Message.ShouldContain("midnight Pacific");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"error":{"code":429}}""")]
    public void A_429_without_details_is_a_per_minute_limit_with_the_header_delay(string body)
    {
        var error = GeminiErrorHandler.Classify(HttpStatusCode.TooManyRequests, body, TimeSpan.FromSeconds(9));

        error.ShouldBeOfType<LlmRateLimitException>().RetryAfter.ShouldBe(TimeSpan.FromSeconds(9));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public void A_busy_server_is_retryable_with_a_busy_message(HttpStatusCode status)
    {
        var error = GeminiErrorHandler.Classify(status, """{"error":{"code":503,"status":"UNAVAILABLE"}}""", TimeSpan.FromSeconds(4));

        var busy = error.ShouldBeOfType<LlmRateLimitException>();
        busy.Message.ShouldBe(GeminiErrorHandler.BusyMessage);
        busy.RetryAfter.ShouldBe(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void A_404_still_passes_through() =>
        GeminiErrorHandler.Classify(HttpStatusCode.NotFound, "", null).ShouldBeNull();

    [Fact]
    public void A_fractional_retry_delay_is_read()
    {
        var body = GeminiErrorBodies.PerMinute.Replace("\"37s\"", "\"2.5s\"", StringComparison.Ordinal);

        GeminiErrorHandler.Classify(HttpStatusCode.TooManyRequests, body, null)
            .ShouldBeOfType<LlmRateLimitException>().RetryAfter.ShouldBe(TimeSpan.FromSeconds(2.5));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void A_401_or_403_means_the_key_was_rejected(HttpStatusCode status)
    {
        GeminiErrorHandler.Classify(status, "", null).ShouldBeOfType<LlmUnauthorizedException>();
    }

    [Fact]
    public void A_400_for_an_invalid_key_means_the_key_was_rejected()
    {
        GeminiErrorHandler.Classify(HttpStatusCode.BadRequest, GeminiErrorBodies.InvalidKey, null).ShouldBeOfType<LlmUnauthorizedException>();
    }

    [Fact]
    public void Other_400s_pass_through()
    {
        GeminiErrorHandler.Classify(HttpStatusCode.BadRequest, """{"error":{"message":"bad image"}}""", null).ShouldBeNull();
    }

    [Fact]
    public async Task A_reply_that_passes_through_keeps_its_body()
    {
        using var handler = new GeminiErrorHandler
        {
            InnerHandler = new FakeHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad image") })),
        };
        using var http = new HttpClient(handler);

        using var response = await http.GetAsync(new Uri("https://example.test/"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("bad image");
    }
}

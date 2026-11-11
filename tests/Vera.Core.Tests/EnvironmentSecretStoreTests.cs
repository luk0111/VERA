namespace Vera.Core.Tests;

public sealed class EnvironmentSecretStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("gemini", "VERA_GEMINI_API_KEY")]
    [InlineData("open-ai", "VERA_OPEN_AI_API_KEY")]
    public void The_variable_name_comes_from_the_secret_name(string name, string variable)
    {
        EnvironmentSecretStore.VariableName(name).ShouldBe(variable);
    }

    [Fact]
    public async Task Get_reads_the_variable()
    {
        var name = "test-" + Guid.NewGuid().ToString("N");
        var variable = EnvironmentSecretStore.VariableName(name);
        Environment.SetEnvironmentVariable(variable, "value-1");
        try
        {
            (await new EnvironmentSecretStore().GetAsync(name, Ct)).ShouldBe("value-1");
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task Get_returns_null_when_the_variable_is_not_set()
    {
        (await new EnvironmentSecretStore().GetAsync("test-" + Guid.NewGuid().ToString("N"), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Set_is_not_supported_and_names_the_variable()
    {
        var error = await Should.ThrowAsync<NotSupportedException>(
            () => new EnvironmentSecretStore().SetAsync("gemini", "secret-value", Ct));

        error.Message.ShouldContain("VERA_GEMINI_API_KEY");
        error.Message.ShouldNotContain("secret-value");
    }
}

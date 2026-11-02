using Vera.Core;

namespace Vera.Core.Tests;

public sealed class SecretPlaceholderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void FindNames_returns_each_name_once_in_order()
    {
        var names = SecretPlaceholder.FindNames("{{secret:user}} / {{secret:pw}} / {{secret:user}}");

        names.ShouldBe(["user", "pw"]);
    }

    [Fact]
    public void FindNames_ignores_text_that_is_not_a_placeholder()
    {
        SecretPlaceholder.FindNames("{{secret:}} {secret:pw} {{password}} plain text").ShouldBeEmpty();
    }

    [Fact]
    public async Task Resolve_replaces_each_placeholder_with_its_value()
    {
        var store = new FakeSecretStore(("user", "admin"), ("pw", "s3cret"));

        var text = await SecretPlaceholder.ResolveAsync("{{secret:user}}:{{secret:pw}}:{{secret:pw}}", store, Ct);

        text.ShouldBe("admin:s3cret:s3cret");
    }

    [Fact]
    public async Task Resolve_leaves_text_without_placeholders_unchanged()
    {
        var text = await SecretPlaceholder.ResolveAsync("hello", new FakeSecretStore(), Ct);

        text.ShouldBe("hello");
    }

    [Fact]
    public async Task Resolve_throws_with_the_name_when_a_secret_is_missing()
    {
        var store = new FakeSecretStore(("user", "admin"));

        var e = await Should.ThrowAsync<SecretNotFoundException>(
            () => SecretPlaceholder.ResolveAsync("{{secret:user}} {{secret:pw}}", store, Ct));

        e.SecretName.ShouldBe("pw");
        e.Message.ShouldNotContain("admin");
    }

    [Fact]
    public async Task Resolve_does_not_resolve_a_value_that_looks_like_a_placeholder()
    {
        var store = new FakeSecretStore(("a", "{{secret:b}}"), ("b", "leaked"));

        var text = await SecretPlaceholder.ResolveAsync("{{secret:a}}", store, Ct);

        text.ShouldBe("{{secret:b}}");
    }

    [Fact]
    public void Mask_hides_every_occurrence_of_each_value()
    {
        var text = SecretPlaceholder.Mask("typed s3cret, then admin, then s3cret", ["s3cret", "admin"]);

        text.ShouldBe("typed ***, then ***, then ***");
    }

    [Fact]
    public void Mask_hides_a_longer_value_whole_when_it_contains_a_shorter_one()
    {
        var text = SecretPlaceholder.Mask("password1", ["pass", "password1"]);

        text.ShouldBe("***");
    }

    [Fact]
    public void Mask_ignores_empty_values()
    {
        SecretPlaceholder.Mask("abc", ["", null!]).ShouldBe("abc");
    }

    private sealed class FakeSecretStore(params (string Name, string Value)[] secrets) : ISecretStore
    {
        public Task<string?> GetAsync(string name, CancellationToken ct) =>
            Task.FromResult(secrets.Where(s => s.Name == name).Select(s => (string?)s.Value).FirstOrDefault());
    }
}
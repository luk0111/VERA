using Microsoft.Extensions.Configuration;

namespace Vera.Application.Tests;

public sealed class RiskRulesTests
{
    [Theory]
    [InlineData("Delete")]
    [InlineData("Save as…")]
    [InlineData("SUBMIT ORDER")]
    [InlineData("Pay now")]
    [InlineData("Remove from cart")]
    [InlineData("Șterge contul")]
    [InlineData("Trimite")]
    [InlineData("salveaza")]
    [InlineData("Checkout")]
    public void A_name_with_a_risky_word_is_risky(string name) =>
        RiskRules.Default.IsRisky(name).ShouldBeTrue();

    [Theory]
    [InlineData("Search")]
    [InlineData("Saved items")]
    [InlineData("Sender")]
    [InlineData("Postcode")]
    [InlineData("")]
    public void Only_whole_words_count(string name) =>
        RiskRules.Default.IsRisky(name).ShouldBeFalse();

    [Theory]
    [InlineData("Pay now", "pay")]
    [InlineData("Buy", "buy")]
    [InlineData("Place order", "place order")]
    [InlineData("Uninstall", "uninstall")]
    [InlineData("Delete my account", "delete my account")]
    [InlineData("Send", "send")]
    [InlineData("Plătește acum", "plătește")]
    [InlineData("Șterge contul", "șterge contul")]
    public void Never_do_finds_the_word_or_phrase(string text, string never) =>
        RiskRules.Default.NeverDoIn(text).ShouldBe(never);

    [Theory]
    [InlineData("Delete")]
    [InlineData("Order history")]
    [InlineData("Format cells")]
    [InlineData("Account")]
    [InlineData("Sender")]
    public void Never_do_leaves_other_names_alone(string text) =>
        RiskRules.Default.NeverDoIn(text).ShouldBeNull();

    [Theory]
    [InlineData("delete", true)]
    [InlineData("Del", true)]
    [InlineData("shift+delete", true)]
    [InlineData("Alt + F4", true)]
    [InlineData("ctrl+w", true)]
    [InlineData("control+w", true)]
    [InlineData("cmd+backspace", true)]
    [InlineData("backspace", false)]
    [InlineData("w", false)]
    [InlineData("alt+w", false)]
    [InlineData("enter", false)]
    public void Keys_that_close_or_delete_are_risky(string keys, bool risky) =>
        RiskRules.Default.IsRiskyKey(keys).ShouldBe(risky);

    [Fact]
    public void Without_a_Safety_section_the_defaults_apply() =>
        RiskRules.FromConfig(Config([])).ShouldBeSameAs(RiskRules.Default);

    [Fact]
    public void The_Safety_section_replaces_the_lists_it_has()
    {
        var rules = RiskRules.FromConfig(Config(new()
        {
            ["Safety:NeverDo:0"] = "Approve invoice",
            ["Safety:AskFirstKeys:0"] = "f5",
        }));

        rules.NeverDo.ShouldBe(["Approve invoice"]);
        rules.NeverDoIn("approve  INVOICE").ShouldBe("approve invoice");
        rules.NeverDoIn("Pay now").ShouldBeNull();
        rules.IsRisky("Delete").ShouldBeTrue();
        rules.IsRiskyKey("F5").ShouldBeTrue();
        rules.IsRiskyKey("delete").ShouldBeFalse();
    }

    [Theory]
    [InlineData("Safety:Typo:0", "pay")]
    [InlineData("Safety:NeverDo:0", " ")]
    [InlineData("Safety:AskFirstKeys:0", "the red button")]
    public void A_bad_Safety_section_is_a_configuration_error(string key, string value) =>
        Should.Throw<VeraConfigurationException>(() => RiskRules.FromConfig(Config(new() { [key] = value })))
            .Message.ShouldStartWith("'Safety' in appsettings.json:");

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}

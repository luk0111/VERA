namespace Vera.Application.Tests;

public sealed class RiskyWordsTests
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
    public void A_name_with_a_risky_word_is_risky(string name) =>
        RiskyWords.IsRisky(name).ShouldBeTrue();

    [Theory]
    [InlineData("Search")]
    [InlineData("Saved items")]
    [InlineData("Sender")]
    [InlineData("Postcode")]
    [InlineData("")]
    public void Only_whole_words_count(string name) =>
        RiskyWords.IsRisky(name).ShouldBeFalse();
}

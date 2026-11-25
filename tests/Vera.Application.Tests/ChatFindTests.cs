namespace Vera.Application.Tests;

public sealed class ChatFindTests
{
    [Theory]
    [InlineData("find the search button", "the search button")]
    [InlineData("Find the cat.", "the cat")]
    [InlineData("find a \"Save\" button", "a \"Save\" button")]
    [InlineData("find the text field", "the text field")]
    [InlineData("find the File menu", "the File menu")]
    [InlineData("find the Issues tab", "the Issues tab")]
    [InlineData("find the Problems panel", "the Problems panel")]
    [InlineData("find the Terms and Conditions link", "the Terms and Conditions link")]
    [InlineData("find the Check out button", "the Check out button")]
    [InlineData("find the open button", "the open button")]
    public void A_short_find_shows_where_it_is(string message, string what) => ChatFind.Asked(message).ShouldBe(what);

    [Theory]
    [InlineData("find the cat, scroll if you have to")]
    [InlineData("find the orange cat even if you have to scroll")]
    [InlineData("find the cat and click it")]
    [InlineData("find the search field, then type cats")]
    [InlineData("find the Save button and check that it works")]
    [InlineData("find the price in the file")]
    [InlineData("find a bug")]
    [InlineData("find the problem with logging in")]
    [InlineData("find a way to check that login works")]
    [InlineData("find the save button?")]
    [InlineData("find out which app this is")]
    [InlineData("what does find the button do")]
    [InlineData("find cats on wikipedia")]
    public void Anything_more_is_for_the_model(string message) => ChatFind.Asked(message).ShouldBeNull();
}

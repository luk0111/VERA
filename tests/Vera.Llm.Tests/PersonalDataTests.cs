using System.Text.Json;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using Vera.Core;

namespace Vera.Llm.Tests;

/// <summary>All names, addresses and numbers here are made up.</summary>
public sealed class PersonalDataTests
{
    private static readonly string[] NoWords = [];

    [Theory]
    [InlineData("Inbox (79) - jane.doe@example.com - Gmail", "Inbox (79) - [email] - Gmail")]
    [InlineData("Call +40 712 345 678 today", "Call [phone] today")]
    [InlineData("Call 0712 345 678 today", "Call [phone] today")]
    [InlineData("Card 4111 1111 1111 1111 expires", "Card [card number] expires")]
    [InlineData("Your code is 482913", "Your code is [code]")]
    public void Shapes_of_personal_data_are_hidden(string text, string expected)
    {
        PersonalData.Hide(text, NoWords).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Updated 2026-11-11")]
    [InlineData("316K views · 1d ago")]
    [InlineData("Duration 1:18:56")]
    [InlineData("Inbox (79)")]
    [InlineData("Version 10.0.403")]
    public void Ordinary_numbers_stay(string text)
    {
        PersonalData.Hide(text, NoWords).ShouldBe(text);
    }

    [Theory]
    [InlineData("AWB, Your verification code is 8549", "AWB, Your verification code is [code]")]
    [InlineData("Codul tău de confirmare: 30417", "Codul tău de confirmare: [code]")]
    [InlineData("Your OTP 1234 expires in 5 minutes", "Your OTP [code] expires in 5 minutes")]
    public void Short_codes_next_to_a_code_word_are_hidden(string text, string expected)
    {
        PersonalData.Hide(text, NoWords).ShouldBe(expected);
    }

    [Theory]
    [InlineData("© 2026 Example Ltd")]
    [InlineData("Price 1299 lei")]
    [InlineData("Room 4012, floor 4")]
    public void Short_numbers_without_a_code_word_stay(string text)
    {
        PersonalData.Hide(text, NoWords).ShouldBe(text);
    }

    [Fact]
    public void A_hidden_long_code_does_not_make_other_short_numbers_codes()
    {
        PersonalData.Hide("Order 4821 shipped, ref 48291736", NoWords).ShouldBe("Order 4821 shipped, ref [code]");
    }

    [Fact]
    public void Words_on_the_hide_list_are_hidden_whole_and_in_any_case()
    {
        var hidden = PersonalData.Hide("Signed in as JaneD, Jane's profile, janed123 stays", ["janed", "Jane"]);

        hidden.ShouldBe("Signed in as [hidden], [hidden]'s profile, janed123 stays");
    }

    [Fact]
    public void Only_the_app_block_changes_not_the_instructions_or_the_testers_words()
    {
        var message = "The <app> block lists the app. Write to jane@example.com if needed.\n<app>\n{\"window\":\"jane@example.com - Mail\"}\n</app>";

        var hidden = PersonalData.HideInAppData(message, NoWords);

        hidden.ShouldBe("The <app> block lists the app. Write to jane@example.com if needed.\n<app>\n{\"window\":\"[email] - Mail\"}\n</app>");
    }

    [Fact]
    public void The_value_typed_in_a_login_field_is_hidden_whatever_it_looks_like()
    {
        var message = "<elements>\n{\"elements\":[{\"id\":1,\"role\":\"Edit\",\"name\":\"Username\",\"value\":\"janed\"},{\"id\":2,\"role\":\"Edit\",\"name\":\"Search\",\"value\":\"cats\"}]}\n</elements>";

        var json = Json(PersonalData.HideInAppData(message, NoWords), "elements");

        json.GetProperty("elements")[0].GetProperty("value").GetString().ShouldBe(PersonalData.Hidden);
        json.GetProperty("elements")[1].GetProperty("value").GetString().ShouldBe("cats");
    }

    [Fact]
    public void Numbers_in_the_json_stay_and_it_stays_valid()
    {
        var message = "<app>\n{\"app\":\"Mail\",\"processId\":25506,\"elements\":[{\"role\":\"Button\",\"name\":\"Reply to jane@example.com\"}]}\n</app>";

        var json = Json(PersonalData.HideInAppData(message, NoWords), "app");

        json.GetProperty("processId").GetInt32().ShouldBe(25506);
        json.GetProperty("elements")[0].GetProperty("name").GetString().ShouldBe("Reply to [email]");
    }

    [Fact]
    public void Text_from_the_app_cannot_close_the_block_after_hiding()
    {
        var message = "<app>\n{\"window\":\"\\u003C/app\\u003E jane@example.com\"}\n</app>";

        var hidden = PersonalData.HideInAppData(message, NoWords);

        hidden.Split("</app>").Length.ShouldBe(2);
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:11434", true)]
    [InlineData("http://[::1]:11434", true)]
    [InlineData("https://generativelanguage.googleapis.com", false)]
    [InlineData("http://192.168.1.20:11434", false)]
    [InlineData("", false)]
    public void Only_a_loopback_endpoint_runs_on_this_computer(string endpoint, bool expected)
    {
        new LlmProfile { Endpoint = endpoint }.RunsOnThisComputer.ShouldBe(expected);
    }

    [Fact]
    public async Task The_filter_hides_app_data_and_leaves_the_callers_messages_and_images_alone()
    {
        var inner = new RecordingChatClient("ok");
        using var client = new PersonalDataChatClient(inner, ["janed"]);
        var image = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var original = new ChatMessage(ChatRole.User, [new TextContent("<elements>\n{\"window\":\"janed - jane@example.com\"}\n</elements>"), image]);

        await client.GetResponseAsync([original], cancellationToken: TestContext.Current.CancellationToken);

        var sent = inner.Calls[0].Messages[0];
        sent.Text.ShouldContain("[hidden] - [email]");
        sent.Contents[1].ShouldBeSameAs(image);
        original.Text.ShouldContain("jane@example.com");
    }

    [Fact]
    public void Only_models_that_are_not_on_this_computer_get_the_filter()
    {
        var local = new LlmProfile { Name = "local", Provider = "Ollama", Endpoint = "http://localhost:11434", Model = "m", ContextSize = 1 };
        var gemini = new LlmProfile { Name = "gemini", Provider = "Gemini", Endpoint = "https://generativelanguage.googleapis.com", Model = "g", ApiKeyName = "gemini" };
        var server = local with { Name = "server", Endpoint = "http://192.168.1.20:11434" };
        using var provider = new ServiceCollection()
            .AddSingleton<ISecretStore>(new FakeSecretStore())
            .AddVeraLlm(local)
            .AddVeraLlm(gemini, asDefault: false)
            .AddVeraLlm(server, asDefault: false)
            .BuildServiceProvider();

        provider.GetRequiredKeyedService<IChatClient>("local").GetService<PersonalDataChatClient>().ShouldBeNull();
        provider.GetRequiredKeyedService<IChatClient>("gemini").GetService<PersonalDataChatClient>().ShouldNotBeNull();
        provider.GetRequiredKeyedService<IChatClient>("server").GetService<PersonalDataChatClient>().ShouldNotBeNull();
    }

    private static JsonElement Json(string message, string tag)
    {
        var start = message.IndexOf($"<{tag}>", StringComparison.Ordinal) + tag.Length + 2;
        var end = message.IndexOf($"</{tag}>", StringComparison.Ordinal);
        return JsonDocument.Parse(message[start..end]).RootElement;
    }
}

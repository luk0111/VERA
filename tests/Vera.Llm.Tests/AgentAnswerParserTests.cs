using Vera.Core;

namespace Vera.Llm.Tests;

public sealed class AgentAnswerParserTests
{
    private static readonly UiElement[] Elements =
    [
        new(0, "Window", "App", new ScreenRect(0, 0, 800, 600), true, "window"),
        new(7, "Button", "Log in", new ScreenRect(10, 10, 80, 30), true, "login"),
    ];

    [Fact]
    public void A_plain_click_is_read()
    {
        var action = AgentAnswerParser.Parse("""{"action":"click","target":7,"text":null,"reason":"It says Log in"}""", Elements);

        action.ShouldBe(new AgentAction(AgentActionType.Click, 7, null, "It says Log in"));
    }

    [Fact]
    public void Json_inside_text_and_fences_is_read()
    {
        var action = AgentAnswerParser.Parse("Sure:\n```json\n{\"action\":\"Click\",\"target\":7,\"reason\":\"r\"}\n```", Elements);

        action.ShouldNotBeNull().Type.ShouldBe(AgentActionType.Click);
        action.TargetId.ShouldBe(7);
    }

    [Fact]
    public void Fail_needs_no_target()
    {
        var action = AgentAnswerParser.Parse("""{"action":"fail","target":null,"reason":"Not there"}""", Elements);

        action.ShouldBe(new AgentAction(AgentActionType.Fail, null, null, "Not there"));
    }

    [Fact]
    public void Press_key_is_read()
    {
        var action = AgentAnswerParser.Parse("""{"action":"press_key","target":null,"text":"Enter","reason":"r"}""", Elements);

        action.ShouldBe(new AgentAction(AgentActionType.PressKey, null, "Enter", "r"));
    }

    [Fact]
    public void A_missing_reason_is_empty()
    {
        AgentAnswerParser.Parse("""{"action":"fail"}""", Elements).ShouldNotBeNull().Reason.ShouldBe("");
    }

    [Fact]
    public void An_unknown_target_is_rejected()
    {
        AgentAnswerParser.Parse("""{"action":"click","target":99,"reason":"r"}""", Elements).ShouldBeNull();
    }

    [Fact]
    public void The_window_cannot_be_the_target()
    {
        AgentAnswerParser.Parse("""{"action":"click","target":0,"reason":"r"}""", Elements).ShouldBeNull();
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        AgentAnswerParser.Parse("I think it's the blue one", Elements).ShouldBeNull();
    }
}

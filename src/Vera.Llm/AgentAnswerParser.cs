using System.Text.Json;

using Vera.Core;

namespace Vera.Llm;

/// <summary>Reads the model's JSON answer into one <see cref="AgentAction"/>, or null when it is not a valid action for this screen.</summary>
internal static class AgentAnswerParser
{
    public static AgentAction? Parse(string reply, IReadOnlyList<UiElement> elements)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(elements);

        // Small models often wrap the JSON in prose or a ```json fence.
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(reply[start..(end + 1)]);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Type(String(root, "action")) is not { } type)
            {
                return null;
            }

            var target = root.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var id) ? id : (int?)null;
            if (type is AgentActionType.Click or AgentActionType.Type && !elements.Any(e => e.Id > 0 && e.Id == target))
            {
                return null;
            }

            return new AgentAction(type, target, String(root, "text"), String(root, "reason") ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static AgentActionType? Type(string? action) => action?.ToUpperInvariant() switch
    {
        "CLICK" => AgentActionType.Click,
        "TYPE" => AgentActionType.Type,
        "PRESS_KEY" => AgentActionType.PressKey,
        "WAIT" => AgentActionType.Wait,
        "DONE" => AgentActionType.Done,
        "FAIL" => AgentActionType.Fail,
        _ => null,
    };
}

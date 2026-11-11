using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vera.Llm;

/// <summary>
/// Hides personal data in the text VERA read from the app under test, before it goes to a model that doesn't run on this computer (#30).
/// It finds what has a fixed shape (email addresses, phone and card numbers, codes), the values of login fields, and the tester's own hide list.
/// Usernames have no shape, so they are hidden only when they are on the hide list or typed in a login field.
/// </summary>
public static partial class PersonalData
{
    public const string Email = "[email]";
    public const string Phone = "[phone]";
    public const string CardNumber = "[card number]";
    public const string Code = "[code]";
    public const string Hidden = "[hidden]";

    /// <summary>
    /// Hides personal data inside the app's data blocks of <paramref name="message"/>: <c>&lt;app&gt;</c> (chat) and <c>&lt;elements&gt;</c> (Find an element, the agent).
    /// Text outside them is left as it is: VERA's own instructions, and what the tester typed on purpose.
    /// </summary>
    public static string HideInAppData(string message, IReadOnlyCollection<string> hideWords)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(hideWords);
        return DataBlock().Replace(message, m =>
            m.Groups["open"].Value + HideInJson(m.Groups["json"].Value, hideWords) + m.Groups["close"].Value);
    }

    /// <summary>Hides emails, phone and card numbers, codes and the words on <paramref name="hideWords"/> anywhere in <paramref name="text"/>.</summary>
    public static string Hide(string text, IReadOnlyCollection<string> hideWords)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(hideWords);

        // Checked before hiding, so the "[code]" label itself doesn't count as a code word.
        var mentionsCode = CodeWord().IsMatch(text);

        // Longest shapes first, so a card number isn't cut up into a phone number and a code.
        text = EmailShape().Replace(text, Email);
        text = CardShape().Replace(text, CardNumber);
        text = PhoneShape().Replace(text, Phone);
        text = CodeShape().Replace(text, Code);

        // Shorter codes look like years or prices, so they are hidden only next to a word that says they are a code.
        if (mentionsCode)
        {
            text = ShortCodeShape().Replace(text, Code);
        }
        foreach (var word in hideWords.Where(w => !string.IsNullOrWhiteSpace(w)))
        {
            text = Regex.Replace(text, $@"(?<!\w){Regex.Escape(word.Trim())}(?!\w)", Hidden, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return text;
    }

    // The blocks hold JSON. Walking it keeps it valid and lets the value of a login field be hidden as a whole.
    private static string HideInJson(string json, IReadOnlyCollection<string> hideWords)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return Hide(json, hideWords);
        }

        if (root is null)
        {
            return json;
        }

        Walk(root, hideWords);
        return root.ToJsonString();
    }

    private static void Walk(JsonNode node, IReadOnlyCollection<string> hideWords)
    {
        switch (node)
        {
            case JsonObject obj:
                // What is typed in a field named like "Username" is the user's name, whatever its shape.
                if (obj["name"] is JsonValue name && name.TryGetValue<string>(out var label) && LoginField().IsMatch(label)
                    && obj["value"] is JsonValue value && value.TryGetValue<string>(out var typed) && typed.Length > 0)
                {
                    obj["value"] = Hidden;
                }

                foreach (var (key, child) in obj.ToList())
                {
                    if (child is JsonValue text && text.TryGetValue<string>(out var s))
                    {
                        obj[key] = Hide(s, hideWords);
                    }
                    else if (child is not null)
                    {
                        Walk(child, hideWords);
                    }
                }

                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue text && text.TryGetValue<string>(out var s))
                    {
                        array[i] = Hide(s, hideWords);
                    }
                    else if (array[i] is { } child)
                    {
                        Walk(child, hideWords);
                    }
                }

                break;
        }
    }

    // Only a tag followed by JSON: VERA's instructions mention "<app>" in plain text too.
    [GeneratedRegex(@"(?<open><(?<tag>app|elements)>\s*)(?<json>\{.*?\})(?<close>\s*</\k<tag>>)", RegexOptions.Singleline)]
    private static partial Regex DataBlock();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailShape();

    // 13 to 19 digits, optionally in groups split by spaces or dashes.
    [GeneratedRegex(@"(?<![\d-])(?:\d[ -]?){12,18}\d(?![\d-])")]
    private static partial Regex CardShape();

    // International numbers start with +; national ones with 0 and have 9 or 10 digits. Dates such as 2026-11-11 match neither.
    [GeneratedRegex(@"(?<![\w+])(?:\+\d{1,3}[ -]?\(?\d{1,4}\)?(?:[ -]?\d{2,4}){2,4}|0\d{2,3}[ -]?\d{3}[ -]?\d{3,4})(?!\d)")]
    private static partial Regex PhoneShape();

    // Verification codes: 6 to 8 digits on their own.
    [GeneratedRegex(@"(?<![\w.,-])\d{6,8}(?![\w.,-])")]
    private static partial Regex CodeShape();

    // 4 or 5 digits on their own. Used only when the same text has a code word.
    [GeneratedRegex(@"(?<![\w.,:/-])\d{4,5}(?![\w.,:/-])")]
    private static partial Regex ShortCodeShape();

    // English and Romanian, the languages the team's apps and mail use.
    [GeneratedRegex(@"\b(code|codes|cod|codul|otp|pin|passcode|verification|verify|verificare|confirmation|confirmare|2fa|one-time|security\s+code)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodeWord();

    [GeneratedRegex(@"\b(user\s*name|username|user|login|log\s*in|e-?mail|account|account\s*name)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoginField();
}

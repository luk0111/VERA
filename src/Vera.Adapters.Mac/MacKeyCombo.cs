using Vera.Input;

namespace Vera.Adapters.Mac;

/// <summary>One Mac key: its virtual key code, and the modifier flag it sets while down (0 for other keys).</summary>
internal readonly record struct MacKey(ushort KeyCode, ulong Flag = 0);

/// <summary>
/// A parsed key combo for macOS, written the same way as on Windows (<c>ctrl+s</c>), so one saved step works on both.
/// <c>ctrl</c> and <c>cmd</c> both press Command, the key Mac apps use where Windows apps use Ctrl. <c>alt</c> is Option.
/// The keys go down in order (modifiers first, the main key last) and come up in reverse.
/// </summary>
/// <param name="Text">The combo as it was understood, e.g. <c>cmd+shift+s</c>. Used for logging.</param>
internal sealed record MacKeyCombo(string Text, IReadOnlyList<MacKey> Keys)
{
    internal const ulong ShiftFlag = 0x20000;
    internal const ulong OptionFlag = 0x80000;
    internal const ulong CommandFlag = 0x100000;

    internal const ushort ReturnKey = 0x24;
    internal const ushort TabKey = 0x30;

    private static readonly string[] ModifierOrder = ["cmd", "shift", "alt"];

    private static readonly Dictionary<string, MacKey> Modifiers = new(StringComparer.Ordinal)
    {
        ["cmd"] = new(0x37, CommandFlag),
        ["shift"] = new(0x38, ShiftFlag),
        ["alt"] = new(0x3A, OptionFlag),
    };

    /// <summary>The keys that can be pressed, with or without modifiers. The same names as on Windows, without <c>insert</c>, which Macs don't have.</summary>
    internal static readonly IReadOnlyDictionary<string, MacKey> MainKeys = BuildKeys();

    /// <summary>Reads a combo like <c>Ctrl+S</c>, <c>cmd+s</c>, <c>enter</c> or <c>alt + f4</c>. Case and spaces don't matter.</summary>
    /// <exception cref="InvalidKeyComboException">Empty, unknown key, two main keys, a repeated modifier, or a key a Mac doesn't have.</exception>
    public static MacKeyCombo Parse(string combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        if (string.IsNullOrWhiteSpace(combo))
        {
            throw Invalid("The key combo is empty.");
        }

        var modifiers = new List<string>();
        string? key = null;
        foreach (var raw in combo.Split('+'))
        {
            var name = raw.Trim().ToLowerInvariant();
            if (name.Length == 0)
            {
                throw Invalid($"'{combo}' has an empty part. Write keys like cmd+s.");
            }

            // Steps written on Windows (ctrl+s) do the same on a Mac (cmd+s).
            if (name == "ctrl")
            {
                name = "cmd";
            }

            if (Modifiers.ContainsKey(name))
            {
                if (modifiers.Contains(name))
                {
                    throw Invalid(name == "cmd"
                        ? $"'{combo}' has cmd twice (ctrl means cmd on a Mac)."
                        : $"'{combo}' has {name} twice.");
                }

                modifiers.Add(name);
            }
            else if (MainKeys.ContainsKey(name))
            {
                if (key is not null)
                {
                    throw Invalid($"'{combo}' has two main keys ({key} and {name}). Press them one after the other.");
                }

                key = name;
            }
            else if (name is "win" or "insert")
            {
                throw Invalid($"'{combo}' uses {name}, which a Mac keyboard doesn't have.");
            }
            else
            {
                throw Invalid($"Unknown key '{raw.Trim()}' in '{combo}'.");
            }
        }

        // Always the same order, so cmd+shift+s and shift+cmd+s press the same thing.
        modifiers.Sort((a, b) => Array.IndexOf(ModifierOrder, a).CompareTo(Array.IndexOf(ModifierOrder, b)));

        var names = key is null ? modifiers : [.. modifiers, key];
        var keys = names.Select(n => Modifiers.TryGetValue(n, out var m) ? m : MainKeys[n]).ToArray();
        return new MacKeyCombo(string.Join('+', names), keys);
    }

    private static InvalidKeyComboException Invalid(string reason) =>
        new($"{reason} Modifiers: cmd (or ctrl), shift, alt. Keys: {string.Join(", ", MainKeys.Keys)}.");

    private static Dictionary<string, MacKey> BuildKeys()
    {
        // Letters and digits by their place on a US keyboard. macOS numbers them in no simple order.
        var keys = new Dictionary<string, MacKey>(StringComparer.Ordinal)
        {
            ["a"] = new(0x00), ["s"] = new(0x01), ["d"] = new(0x02), ["f"] = new(0x03), ["h"] = new(0x04),
            ["g"] = new(0x05), ["z"] = new(0x06), ["x"] = new(0x07), ["c"] = new(0x08), ["v"] = new(0x09),
            ["b"] = new(0x0B), ["q"] = new(0x0C), ["w"] = new(0x0D), ["e"] = new(0x0E), ["r"] = new(0x0F),
            ["y"] = new(0x10), ["t"] = new(0x11), ["o"] = new(0x1F), ["u"] = new(0x20), ["i"] = new(0x22),
            ["p"] = new(0x23), ["l"] = new(0x25), ["j"] = new(0x26), ["k"] = new(0x28), ["n"] = new(0x2D),
            ["m"] = new(0x2E),

            ["1"] = new(0x12), ["2"] = new(0x13), ["3"] = new(0x14), ["4"] = new(0x15), ["5"] = new(0x17),
            ["6"] = new(0x16), ["7"] = new(0x1A), ["8"] = new(0x1C), ["9"] = new(0x19), ["0"] = new(0x1D),

            ["f1"] = new(0x7A), ["f2"] = new(0x78), ["f3"] = new(0x63), ["f4"] = new(0x76), ["f5"] = new(0x60),
            ["f6"] = new(0x61), ["f7"] = new(0x62), ["f8"] = new(0x64), ["f9"] = new(0x65), ["f10"] = new(0x6D),
            ["f11"] = new(0x67), ["f12"] = new(0x6F),

            ["enter"] = new(ReturnKey),
            ["tab"] = new(TabKey),
            ["esc"] = new(0x35),
            ["space"] = new(0x31),

            // A Mac's "delete" key erases backwards like Backspace; forward delete is its own key.
            ["backspace"] = new(0x33),
            ["delete"] = new(0x75),

            ["home"] = new(0x73),
            ["end"] = new(0x77),
            ["pageup"] = new(0x74),
            ["pagedown"] = new(0x79),
            ["left"] = new(0x7B),
            ["right"] = new(0x7C),
            ["down"] = new(0x7D),
            ["up"] = new(0x7E),
        };
        return keys;
    }
}

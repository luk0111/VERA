namespace Vera.Input.Injection;

/// <summary>A physical key: its set-1 scancode, and whether it is one of the extended keys (arrows, Home, Win…).</summary>
internal readonly record struct ScanKey(ushort ScanCode, bool Extended = false);

/// <summary>
/// A parsed key combo such as <c>ctrl+shift+s</c>: the keys in the order they go down (modifiers first, the main key
/// last). They come up in reverse.
/// </summary>
/// <param name="Text">The combo as it was understood, e.g. <c>ctrl+shift+s</c>. Used for logging.</param>
internal sealed record KeyCombo(string Text, IReadOnlyList<ScanKey> Keys)
{
    private static readonly string[] ModifierOrder = ["ctrl", "shift", "alt", "win"];

    private static readonly Dictionary<string, ScanKey> Modifiers = new(StringComparer.Ordinal)
    {
        ["ctrl"] = new(0x1D),
        ["shift"] = new(0x2A),
        ["alt"] = new(0x38),
        ["win"] = new(0x5B, Extended: true),
    };

    /// <summary>The keys that can be pressed, with or without modifiers.</summary>
    internal static readonly IReadOnlyDictionary<string, ScanKey> MainKeys = BuildKeys();

    /// <summary>Reads a combo like <c>Ctrl+S</c>, <c>enter</c> or <c>alt + f4</c>. Case and spaces don't matter.</summary>
    /// <exception cref="InvalidKeyComboException">Empty, unknown key, two main keys, a repeated modifier, or a refused combo.</exception>
    public static KeyCombo Parse(string combo)
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
                throw Invalid($"'{combo}' has an empty part. Write keys like ctrl+s.");
            }

            if (Modifiers.ContainsKey(name))
            {
                if (modifiers.Contains(name))
                {
                    throw Invalid($"'{combo}' has {name} twice.");
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
            else
            {
                throw Invalid($"Unknown key '{raw.Trim()}' in '{combo}'.");
            }
        }

        // Always the same order, so ctrl+shift+s and shift+ctrl+s press the same thing.
        modifiers.Sort((a, b) => Array.IndexOf(ModifierOrder, a).CompareTo(Array.IndexOf(ModifierOrder, b)));

        if (key == "delete" && modifiers.Contains("ctrl") && modifiers.Contains("alt"))
        {
            throw Invalid("ctrl+alt+delete can't be sent: Windows ignores it from programs.");
        }

        if (key == "l" && modifiers.Contains("win"))
        {
            throw Invalid("win+l is refused: it would lock the PC and end the run.");
        }

        var names = key is null ? modifiers : [.. modifiers, key];
        var keys = names.Select(n => Modifiers.TryGetValue(n, out var m) ? m : MainKeys[n]).ToArray();
        return new KeyCombo(string.Join('+', names), keys);
    }

    private static InvalidKeyComboException Invalid(string reason) =>
        new($"{reason} Modifiers: {string.Join(", ", ModifierOrder)}. Keys: {string.Join(", ", MainKeys.Keys)}.");

    private static Dictionary<string, ScanKey> BuildKeys()
    {
        var keys = new Dictionary<string, ScanKey>(StringComparer.Ordinal);

        // Letters by their place on a US keyboard, row by row.
        AddRow(keys, "qwertyuiop", 0x10);
        AddRow(keys, "asdfghjkl", 0x1E);
        AddRow(keys, "zxcvbnm", 0x2C);

        // 1–9 then 0, along the top row.
        AddRow(keys, "1234567890", 0x02);

        for (var i = 1; i <= 10; i++)
        {
            keys[$"f{i}"] = new((ushort)(0x3A + i));
        }

        keys["f11"] = new(0x57);
        keys["f12"] = new(0x58);

        keys["enter"] = new(KeyboardDriver.EnterScanCode);
        keys["tab"] = new(KeyboardDriver.TabScanCode);
        keys["esc"] = new(0x01);
        keys["space"] = new(0x39);
        keys["backspace"] = new(0x0E);

        // The grey keys between the letters and the numpad. Without the extended flag they would be the numpad keys.
        keys["insert"] = new(0x52, Extended: true);
        keys["delete"] = new(0x53, Extended: true);
        keys["home"] = new(0x47, Extended: true);
        keys["end"] = new(0x4F, Extended: true);
        keys["pageup"] = new(0x49, Extended: true);
        keys["pagedown"] = new(0x51, Extended: true);
        keys["up"] = new(0x48, Extended: true);
        keys["down"] = new(0x50, Extended: true);
        keys["left"] = new(0x4B, Extended: true);
        keys["right"] = new(0x4D, Extended: true);

        return keys;
    }

    private static void AddRow(Dictionary<string, ScanKey> keys, string row, ushort first)
    {
        for (var i = 0; i < row.Length; i++)
        {
            keys[row[i].ToString()] = new((ushort)(first + i));
        }
    }
}

using System.Text.RegularExpressions;

using Microsoft.Extensions.Configuration;

namespace Vera.Application;

/// <summary>
/// The rules VERA checks before it acts in the app (#38), from the <c>Safety</c> section of appsettings.json; <see cref="Default"/> without one.
/// Words and phrases match whole words, in any case: "Save as" matches <c>save</c>, "Saved" doesn't; "Delete my account" matches <c>delete my account</c>.
/// </summary>
public sealed partial class RiskRules
{
    public const string SectionName = "Safety";

    /// <summary>
    /// What VERA never does in steps the AI proposed: payments, purchases, uninstalling, formatting, deleting an account, sending messages.
    /// Steps the tester writes, records or loads only ask first for these.
    /// </summary>
    internal static readonly string[] DefaultNeverDo =
    [
        "pay", "buy", "purchase", "checkout", "check out", "place order", "order now",
        "uninstall", "format disk", "format drive",
        "delete account", "delete my account", "delete your account", "close account", "close my account",
        "send",

        // Romanian, with and without diacritics.
        "plătește", "plateste", "plătiți", "platiti", "cumpără", "cumpara", "cumpărați", "cumparati",
        "plasează comanda", "plaseaza comanda", "finalizează comanda", "finalizeaza comanda",
        "dezinstalează", "dezinstaleaza", "șterge contul", "sterge contul", "închide contul", "inchide contul", "trimite",
    ];

    /// <summary>Words that make a click or a drop ask first, when Settings' "Ask before risky actions" is on.</summary>
    internal static readonly string[] DefaultAskFirst =
    [
        "delete", "remove", "erase", "discard", "save", "submit", "send", "pay", "buy", "purchase", "order",
        "confirm", "publish", "post", "uninstall", "reset", "format",
        "șterge", "sterge", "salvează", "salveaza", "trimite", "plătește", "plateste", "cumpără", "cumpara", "confirmă", "confirma",
    ];

    /// <summary>Keys that ask first. A key alone counts with any other keys held (<c>delete</c> covers <c>shift+delete</c>); a combo counts only as written.</summary>
    internal static readonly string[] DefaultAskFirstKeys =
    [
        "delete", "shift+delete", "ctrl+backspace", "shift+backspace", "alt+backspace", "cmd+backspace", "alt+f4", "ctrl+w",
    ];

    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.Ordinal)
    {
        ["control"] = "ctrl", ["del"] = "delete", ["option"] = "alt", ["command"] = "cmd",
    };

    private readonly List<string[]> _neverDo;
    private readonly List<string[]> _askFirst;
    private readonly List<string[]> _askFirstKeys;

    public RiskRules(IEnumerable<string> neverDo, IEnumerable<string> askFirst, IEnumerable<string> askFirstKeys)
    {
        ArgumentNullException.ThrowIfNull(neverDo);
        ArgumentNullException.ThrowIfNull(askFirst);
        ArgumentNullException.ThrowIfNull(askFirstKeys);
        NeverDo = [.. neverDo];
        _neverDo = Phrases(NeverDo, "NeverDo");
        _askFirst = Phrases(askFirst, "AskFirst");
        _askFirstKeys = [.. askFirstKeys.Select(Key)];
    }

    public static RiskRules Default { get; } = new(DefaultNeverDo, DefaultAskFirst, DefaultAskFirstKeys);

    /// <summary>The never-do list as written in config, to show the tester.</summary>
    public IReadOnlyList<string> NeverDo { get; }

    /// <summary>Whether clicking or dropping onto an element with this name should ask first: a word of either list is in it.</summary>
    public bool IsRisky(string name) => NeverDoIn(name) is not null || Find(_askFirst, name) is not null;

    /// <summary>The never-do word or phrase in <paramref name="text"/>, e.g. <c>pay</c> in "Pay now"; null when there is none.</summary>
    public string? NeverDoIn(string text) => Find(_neverDo, text) is { } phrase ? string.Join(' ', phrase) : null;

    /// <summary>Whether pressing <paramref name="keys"/>, e.g. <c>Alt + F4</c>, should ask first.</summary>
    public bool IsRiskyKey(string keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var pressed = KeyParts(keys);
        return pressed.Length > 0 && _askFirstKeys.Any(rule => rule.Length == 1
            ? rule[0] == pressed[^1]
            : rule.Order(StringComparer.Ordinal).SequenceEqual(pressed.Order(StringComparer.Ordinal)));
    }

    /// <summary>Reads the <c>Safety</c> section; <see cref="Default"/> when there is none. A list left out keeps its default.</summary>
    /// <exception cref="VeraConfigurationException">The section has an unknown setting, an empty entry, or a key that isn't one.</exception>
    public static RiskRules FromConfig(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var section = config.GetSection(SectionName);
        if (!section.Exists())
        {
            return Default;
        }

        var options = new SafetyOptions();
        try
        {
            section.Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);
            return new RiskRules(options.NeverDo ?? DefaultNeverDo, options.AskFirst ?? DefaultAskFirst, options.AskFirstKeys ?? DefaultAskFirstKeys);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            throw new VeraConfigurationException($"'{SectionName}' in appsettings.json: {e.Message}", e);
        }
    }

    private static List<string[]> Phrases(IEnumerable<string> entries, string list) =>
        [.. entries.Select(entry => Words(entry ?? "") is { Length: > 0 } words
            ? words
            : throw new ArgumentException($"{list} has an empty entry."))];

    private static string[] Key(string entry)
    {
        var keys = (entry ?? "").Trim();
        return StepParser.IsKeyName(keys) || StepParser.IsCombo(keys)
            ? KeyParts(keys)
            : throw new ArgumentException($"AskFirstKeys: \"{entry}\" isn't a key, e.g. delete or alt+f4.");
    }

    private static string[] KeyParts(string keys) =>
        [.. keys.ToLowerInvariant().Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(k => KeyAliases.GetValueOrDefault(k, k))];

    // The first phrase whose words appear one after another in the text.
    private static string[]? Find(List<string[]> phrases, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = Words(text);
        return phrases.FirstOrDefault(phrase => Enumerable.Range(0, Math.Max(0, words.Length - phrase.Length + 1))
            .Any(start => words.AsSpan(start, phrase.Length).SequenceEqual(phrase)));
    }

    private static string[] Words(string text) =>
        NotALetter().Split(text.ToLowerInvariant()).Where(w => w.Length > 0).ToArray();

    [GeneratedRegex(@"[^\p{L}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NotALetter();

    /// <summary>The <c>Safety</c> section as written. Null means the list was left out.</summary>
    private sealed class SafetyOptions
    {
        public string[]? NeverDo { get; set; }

        public string[]? AskFirst { get; set; }

        public string[]? AskFirstKeys { get; set; }
    }
}

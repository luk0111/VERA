using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Vera.Adapters.Desktop;

/// <summary>One thing the tester did while VERA recorded (#48).</summary>
public abstract record RecordedAction;

/// <summary>A click on an element, e.g. a <c>Button</c> named <c>Save</c>.</summary>
public sealed record RecordedClick(string Role, string Name) : RecordedAction;

/// <summary>The text a field holds after the tester typed in it.</summary>
public sealed record RecordedType(string Role, string Name, string Text) : RecordedAction;

/// <summary>A key or combo in VERA's names, e.g. <c>enter</c> or <c>ctrl+s</c>.</summary>
public sealed record RecordedPress(string Keys) : RecordedAction;

/// <summary>Something the tester did that VERA can't write as a step yet, with the reason to show them.</summary>
public sealed record RecordedSkip(string Reason) : RecordedAction;

/// <summary>What the platform's hook saw: a click or a key.</summary>
internal abstract record HookInput;

/// <summary>A left click the platform's hook saw, in the coordinates its element reader uses.</summary>
internal sealed record ClickInput(int X, int Y) : HookInput;

/// <summary>A key going down in the app, by VERA's key name (null for keys VERA has no name for), with the modifiers held.</summary>
internal sealed record KeyInput(string? Key, bool Ctrl, bool Shift, bool Alt) : HookInput;

/// <summary>Where the keyboard is: in a one-line field, a text area (where Enter and Tab are part of the text), or elsewhere.</summary>
internal enum Focus
{
    Other,
    Field,
    TextArea,
}

/// <summary>Reads the app's elements for the recorder, one implementation per platform. An element that is gone reads as null or empty.</summary>
internal interface IRecordedElements<TElement>
    where TElement : class
{
    /// <summary>The element under the point a tester could act on (a button rather than the text inside it), if it is the app's.</summary>
    TElement? At(int x, int y);

    /// <summary>The app's text field that has the keyboard focus, unless it is a password field.</summary>
    TElement? FocusedTextField();

    /// <summary>The role in VERA's words, e.g. <c>Button</c> or <c>Edit</c>.</summary>
    string Role(TElement element);

    string Name(TElement element);

    bool IsPassword(TElement element);

    /// <summary>Whether Enter and Tab type into it (Notepad's page, a Mac text area).</summary>
    bool IsTextArea(TElement element);

    /// <summary>The text inside, or null when it can't be read.</summary>
    string? Text(TElement element);
}

/// <summary>
/// Turns what the tester did into recorded actions, the same way on every platform. The platform's hook gives clicks and keys;
/// typing is not caught key by key: when the tester leaves a text field, its text is read.
/// </summary>
internal static class StepRecording
{
    private static readonly HashSet<string> TextRoles = new(StringComparer.Ordinal) { "Edit", "Document", "ComboBox" };

    /// <summary>Reads the hook's events until the channel ends, then still gives the text of the field the tester was typing in.</summary>
    public static async IAsyncEnumerable<RecordedAction> RecordAsync<TElement>(
        ChannelReader<HookInput> input,
        IRecordedElements<TElement> elements,
        [EnumeratorCancellation] CancellationToken ct = default)
        where TElement : class
    {
        // The text field being typed in, and its text when VERA last looked (null: not read yet, so give it at the end anyway).
        var field = (TElement?)null;
        var fieldText = (string?)null;

        await foreach (var next in input.ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (next is KeyInput key)
            {
                // Typing can start without a click, e.g. in a field that had the focus already or one reached with Tab.
                if (field is null && elements.FocusedTextField() is { } focused)
                {
                    (field, fieldText) = (focused, null);
                }

                var focus = field is null ? Focus.Other : elements.IsTextArea(field) ? Focus.TextArea : Focus.Field;
                if (key.Key is null || KeyName(key.Key, key.Ctrl, key.Shift, key.Alt, focus) is not { } keys)
                {
                    continue;
                }

                if (Typed(elements, field, ref fieldText) is { } typed)
                {
                    yield return typed;
                }

                // The key may move the focus (Tab) or close the field (Esc); the next typing finds the field again.
                field = null;
                yield return new RecordedPress(keys);
            }
            else if (next is ClickInput click)
            {
                if (Typed(elements, field, ref fieldText) is { } typed)
                {
                    yield return typed;
                }

                field = null;
                if (elements.At(click.X, click.Y) is not { } element)
                {
                    continue;
                }

                var (role, name) = (elements.Role(element), elements.Name(element));
                if (string.IsNullOrWhiteSpace(name))
                {
                    yield return new RecordedSkip($"Skipped a click on a {role} with no name.");
                    continue;
                }

                yield return new RecordedClick(role, name);

                // A password field's text is never read (#30).
                if (elements.IsPassword(element))
                {
                    yield return new RecordedSkip($"VERA doesn't record typing into the password field \"{name}\". Add a step like: type \"{{{{secret:name}}}}\" into it.");
                }
                else if (TextRoles.Contains(role))
                {
                    (field, fieldText) = (element, elements.Text(element));
                }
            }
        }

        if (Typed(elements, field, ref fieldText) is { } last)
        {
            yield return last;
        }
    }

    /// <summary>
    /// The key as a <c>press</c> step writes it, or null when it isn't one: plain typing (the field's text covers it)
    /// and editing keys inside a text field. Enter, Tab, Esc, F1–F12 and Ctrl/Alt combos count.
    /// </summary>
    /// <param name="key">VERA's name for the key, e.g. <c>s</c> or <c>enter</c>.</param>
    internal static string? KeyName(string key, bool ctrl, bool shift, bool alt, Focus focus)
    {
        var isAction = key is "esc" || IsFunctionKey(key)
            || (key is "enter" or "tab" && focus != Focus.TextArea)
            || alt
            || (ctrl && !(focus != Focus.Other && IsEditingKey(key)));

        return isAction ? (ctrl ? "ctrl+" : "") + (shift ? "shift+" : "") + (alt ? "alt+" : "") + key : null;
    }

    private static bool IsFunctionKey(string key) => key.Length is 2 or 3 && key[0] == 'f' && char.IsAsciiDigit(key[1]);

    // Ctrl with these only edits the text being typed (select all, copy, paste, undo, move by word), which the field's text already shows.
    private static bool IsEditingKey(string key) =>
        key is "a" or "c" or "v" or "x" or "z" or "y" or "left" or "right" or "up" or "down" or "home" or "end" or "backspace" or "delete";

    // The field's text, as a step if it changed since VERA last looked.
    private static RecordedAction? Typed<TElement>(IRecordedElements<TElement> elements, TElement? field, ref string? lastText)
        where TElement : class
    {
        if (field is null || elements.Text(field) is not { } text || text == lastText)
        {
            return null;
        }

        lastText = text;
        var (role, name) = (elements.Role(field), elements.Name(field));
        return text.Contains('\n', StringComparison.Ordinal) || text.Contains('\r', StringComparison.Ordinal)
            ? new RecordedSkip($"Skipped typing into {role} \"{name}\": text with several lines can't be a step yet.")
            : new RecordedType(role, name, text);
    }
}

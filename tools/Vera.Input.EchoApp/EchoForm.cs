using System.Text;

namespace Vera.Input.EchoApp;

/// <summary>
/// A borderless window over the whole primary monitor (the one VERA captures by default). It lists the last mouse
/// button events with their screen position and the characters it receives, and draws a crosshair where the last
/// button went down. The typed text is shown on one line, cleared by a click. Esc closes it.
/// </summary>
internal sealed class EchoForm : Form
{
    private const int MaxEvents = 20;

    private readonly List<string> _events = [];
    private readonly StringBuilder _typed = new();
    private readonly Font _font = new("Consolas", 14);
    private Point? _lastDown;
    private char? _highSurrogate;

    public EchoForm()
    {
        Text = "VERA EchoApp";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? Screen.FromPoint(Cursor.Position).Bounds;
        BackColor = Color.FromArgb(24, 24, 24);
        ForeColor = Color.Gainsboro;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _lastDown = e.Location;
        _typed.Clear();
        _highSurrogate = null;
        Add("Down", e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Add("Up", e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        Add("DoubleClick", e);
    }

    /// <summary>Lets Tab and Enter arrive as characters instead of moving the focus or pressing a default button.</summary>
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Tab or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        var c = e.KeyChar;
        _typed.Append(c);

        if (char.IsHighSurrogate(c))
        {
            // The first half of an emoji or similar: log it together with the second half.
            _highSurrogate = c;
            Invalidate();
            return;
        }

        if (char.IsLowSurrogate(c) && _highSurrogate is { } high)
        {
            _highSurrogate = null;
            AddLine($"Char         {high}{c}  U+{char.ConvertToUtf32(high, c):X4}");
            return;
        }

        _highSurrogate = null;
        AddLine($"Char         {Describe(c)}  U+{(int)c:X4}");
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;

        if (_lastDown is { } p)
        {
            using var pen = new Pen(Color.OrangeRed);
            g.DrawLine(pen, p.X, 0, p.X, ClientSize.Height);
            g.DrawLine(pen, 0, p.Y, ClientSize.Width, p.Y);
        }

        var lines = new List<string>
        {
            $"VERA EchoApp on {Bounds.Width}×{Bounds.Height} at ({Bounds.X},{Bounds.Y}). Click anywhere or type. A click clears the text. Esc closes.",
            "",
            $"Typed ({_typed.Length}): {Show(_typed.ToString())}",
            "",
        };
        lines.AddRange(_events);

        var y = 20f;
        foreach (var line in lines)
        {
            TextRenderer.DrawText(g, line, _font, new Point(20, (int)y), ForeColor);
            y += _font.GetHeight(g) * 1.2f;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _font.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>The text on one line, with Enter shown as ⏎ and Tab as →, like the harness's "Will type" line.</summary>
    private static string Show(string text) =>
        text.Replace("\r", "⏎", StringComparison.Ordinal).Replace("\t", "→", StringComparison.Ordinal);

    private static string Describe(char c) => c switch
    {
        '\r' => "⏎ Enter",
        '\t' => "→ Tab",
        ' ' => "space",
        _ when char.IsControl(c) => "control",
        _ => c.ToString(),
    };

    private void Add(string kind, MouseEventArgs e)
    {
        var screen = PointToScreen(e.Location);
        AddLine($"{kind,-11}  {e.Button,-6}  screen ({screen.X},{screen.Y})");
    }

    private void AddLine(string line)
    {
        _events.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {line}");
        if (_events.Count > MaxEvents)
        {
            _events.RemoveAt(_events.Count - 1);
        }

        Invalidate();
    }
}

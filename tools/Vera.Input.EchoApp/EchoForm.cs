namespace Vera.Input.EchoApp;

/// <summary>
/// A borderless window over the whole primary monitor (the one VERA captures by default). It lists the last mouse
/// button events with their screen position, and draws a crosshair where the last button went down. Esc closes it.
/// </summary>
internal sealed class EchoForm : Form
{
    private const int MaxEvents = 20;

    private readonly List<string> _events = [];
    private readonly Font _font = new("Consolas", 14);
    private Point? _lastDown;

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
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
            $"VERA EchoApp on {Bounds.Width}×{Bounds.Height} at ({Bounds.X},{Bounds.Y}). Click anywhere. Esc closes.",
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

    private void Add(string kind, MouseEventArgs e)
    {
        var screen = PointToScreen(e.Location);
        _events.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {kind,-11}  {e.Button,-6}  screen ({screen.X},{screen.Y})");
        if (_events.Count > MaxEvents)
        {
            _events.RemoveAt(_events.Count - 1);
        }

        Invalidate();
    }
}

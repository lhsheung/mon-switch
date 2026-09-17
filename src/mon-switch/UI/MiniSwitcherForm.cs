using System.Drawing;
using System.Windows.Forms;

namespace MonSwitch.UI;

/// <summary>
/// A small window that stays reachable on whichever screen it is placed on.
///
/// Why this exists rather than a second tray icon: the notification area belongs to Explorer,
/// and Windows only ever shows it on one taskbar. An application cannot ask for its own icon to
/// be drawn on another monitor - that decision lives entirely inside Explorer. A window of our
/// own is the only thing mon-switch can place freely, so that is what this is.
///
/// It is deliberately a thin shell: it owns its pixels and its dragging, and raises events for
/// everything that involves application state. The tray context owns the windows and decides
/// what "cycle", "menu" and "current mode" mean.
/// </summary>
internal sealed class MiniSwitcherForm : Form
{
    private const int DragThreshold = 4;

    private readonly Label _label = new();
    private Point _pressPoint;
    private bool _pressActive;
    private bool _dragged;

    /// <summary>Device name of the screen this copy belongs to, used as the placement key.</summary>
    public string ScreenKey { get; }

    public event EventHandler? RequestCycle;

    /// <summary>Raised on a right click. The point is in client coordinates.</summary>
    public event EventHandler<Point>? RequestMenu;

    /// <summary>Raised after the user finishes dragging, so the new position can be stored.</summary>
    public event EventHandler? Moved;

    public MiniSwitcherForm(string screenKey, Point location, bool topMost, double opacity)
    {
        ScreenKey = screenKey;

        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;

        // Borderless, so the title is never painted - but a window with no title is harder to
        // find with diagnostic tools, and screen readers announce it. It costs nothing.
        Text = "mon-switch";

        StartPosition = FormStartPosition.Manual;
        Location = location;
        TopMost = topMost;
        Opacity = opacity;
        MinimumSize = new Size(40, 28);
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, SystemFonts.MessageBoxFont.SizeInPoints);

        _label.AutoSize = true;
        _label.Location = new Point(10, 0);
        _label.Font = Font;
        _label.ForeColor = ForeColor;
        _label.BackColor = Color.Transparent;
        _label.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(_label);

        // Every mouse handler is wired to the label as well as the form, otherwise a press that
        // lands on the text would do nothing.
        foreach (Control target in new Control[] { this, _label })
        {
            target.MouseDown += OnMouseDownAny;
            target.MouseMove += OnMouseMoveAny;
            target.MouseUp += OnMouseUpAny;
        }

        ResumeLayout(performLayout: true);
    }

    /// <summary>Sets the visible text and widens the window to fit, keeping the top-left put.</summary>
    public void SetContent(string text)
    {
        if (_label.Text == text)
        {
            return;
        }

        _label.Text = text;

        int wanted = _label.Left + _label.PreferredWidth + 10;
        if (wanted < MinimumSize.Width)
        {
            wanted = MinimumSize.Width;
        }

        if (wanted != Width)
        {
            Width = wanted;
        }

        Height = Math.Max(MinimumSize.Height, _label.PreferredHeight + 8);
        _label.Top = (Height - _label.PreferredHeight) / 2;
    }

    public void ApplyLook(bool topMost, double opacity)
    {
        TopMost = topMost;
        Opacity = opacity;

        // Toggling TopMost alone does not always re-order the window, so it is nudged.
        if (IsHandleCreated && Visible)
        {
            TopMost = !topMost;
            TopMost = topMost;
        }
    }

    private void OnMouseDownAny(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            RequestMenu?.Invoke(this, ToClientFromScreen(Cursor.Position));
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _pressActive = true;
        _dragged = false;
        _pressPoint = Cursor.Position;
        Capture = true;
    }

    private void OnMouseMoveAny(object? sender, MouseEventArgs e)
    {
        if (!_pressActive)
        {
            return;
        }

        Point now = Cursor.Position;
        int dx = now.X - _pressPoint.X;
        int dy = now.Y - _pressPoint.Y;

        if (!_dragged && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
        {
            return;
        }

        _dragged = true;
        Left += dx;
        Top += dy;
        _pressPoint = now;
    }

    private void OnMouseUpAny(object? sender, MouseEventArgs e)
    {
        if (!_pressActive)
        {
            return;
        }

        _pressActive = false;
        Capture = false;

        if (_dragged)
        {
            Moved?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Button == MouseButtons.Left)
        {
            RequestCycle?.Invoke(this, EventArgs.Empty);
        }
    }

    private Point ToClientFromScreen(Point screen) => PointToClient(screen);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // A borderless form has no frame, so draw one: without it the window reads as a stray
        // patch of colour rather than something you can pick up.
        using var pen = new Pen(SystemColors.ActiveBorder);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _label.Dispose();
        }

        base.Dispose(disposing);
    }
}

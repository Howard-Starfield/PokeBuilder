using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms;

internal sealed class ConfigurationSearchBox : UserControl
{
    private readonly TextBox _textBox;
    private readonly Button _clearButton;
    private readonly Font _ownedFont;
    private bool _hot;

    public ConfigurationSearchBox()
    {
        AccessibleName = "Search configuration settings";
        AccessibleRole = AccessibleRole.Text;
        BackColor = ConfigurationTheme.Canvas;
        Cursor = Cursors.IBeam;
        Height = 34;
        TabStop = false;

        _ownedFont = new Font("Segoe UI", 9F);
        _textBox = new TextBox
        {
            AccessibleName = "Search configuration settings",
            BackColor = ConfigurationTheme.Canvas,
            BorderStyle = BorderStyle.None,
            Font = _ownedFont,
            ForeColor = ConfigurationTheme.TextPrimary,
            PlaceholderText = "Search settings...",
            TabIndex = 0,
        };
        _clearButton = new Button
        {
            AccessibleName = "Clear configuration search",
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 10F),
            ForeColor = ConfigurationTheme.TextMuted,
            TabIndex = 1,
            Text = "\u00d7",
            Visible = false,
        };
        _clearButton.FlatAppearance.BorderSize = 0;
        _clearButton.FlatAppearance.MouseDownBackColor = ConfigurationTheme.SurfaceSelected;
        _clearButton.FlatAppearance.MouseOverBackColor = ConfigurationTheme.SurfaceHover;

        _textBox.TextChanged += (_, _) =>
        {
            _clearButton.Visible = _textBox.TextLength > 0;
            LayoutChildren();
            SearchTextChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        };
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape || _textBox.TextLength == 0)
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            _textBox.Clear();
        };
        _textBox.Enter += (_, _) => Invalidate();
        _textBox.Leave += (_, _) => Invalidate();
        _clearButton.Click += (_, _) =>
        {
            _textBox.Clear();
            _textBox.Focus();
        };

        Controls.Add(_textBox);
        Controls.Add(_clearButton);
        Resize += (_, _) => LayoutChildren();
        MouseEnter += (_, _) =>
        {
            _hot = true;
            Invalidate();
        };
        MouseLeave += (_, _) =>
        {
            _hot = ClientRectangle.Contains(PointToClient(Cursor.Position));
            Invalidate();
        };
        Click += (_, _) => _textBox.Focus();
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    public event EventHandler? SearchTextChanged;
    public string SearchText => _textBox.Text;

    public void Clear() => _textBox.Clear();

    public void FocusSearch()
    {
        _textBox.Focus();
        _textBox.SelectAll();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = new GraphicsPath())
        using (var fill = new SolidBrush(ConfigurationTheme.Canvas))
        using (var border = new Pen(
                   _textBox.Focused
                       ? ConfigurationTheme.Accent
                       : _hot
                           ? ConfigurationTheme.BorderStrong
                           : ConfigurationTheme.Border))
        {
            path.AddRoundedRectangle(bounds, 7);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        var iconBounds = new Rectangle(11, (Height - 14) / 2, 13, 13);
        using var icon = new Pen(
            _textBox.Focused ? ConfigurationTheme.Accent : ConfigurationTheme.TextMuted,
            1.6F);
        e.Graphics.DrawEllipse(icon, iconBounds.X, iconBounds.Y, 8, 8);
        e.Graphics.DrawLine(
            icon,
            iconBounds.X + 7,
            iconBounds.Y + 7,
            iconBounds.Right,
            iconBounds.Bottom);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ownedFont.Dispose();
            _clearButton.Font.Dispose();
        }
        base.Dispose(disposing);
    }

    private void LayoutChildren()
    {
        var clearWidth = _clearButton.Visible ? 28 : 0;
        _clearButton.Bounds = new Rectangle(
            Math.Max(0, Width - 31),
            3,
            28,
            Math.Max(24, Height - 6));
        _textBox.Bounds = new Rectangle(
            33,
            Math.Max(2, (Height - _textBox.PreferredHeight) / 2),
            Math.Max(30, Width - 43 - clearWidth),
            _textBox.PreferredHeight);
    }
}

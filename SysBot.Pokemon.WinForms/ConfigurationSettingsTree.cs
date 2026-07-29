using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using SysBot.Pokemon.Helpers;

namespace SysBot.Pokemon.WinForms;

internal sealed class ConfigurationSettingsTree : UserControl
{
    private readonly FlowLayoutPanel _content;
    private object? _view;
    private IReadOnlyList<ConfigurationSearchResult>? _searchResults;
    private string _searchQuery = string.Empty;
    private Action? _valueChanged;
    private Action? _openAdvanced;
    private IReadOnlyList<ConfigurationCategoryAction>? _actions;
    private readonly HashSet<string> _expandedGroupKeys = [];
    private bool _expansionStateInitialized;
    private bool _rebuildPending;
    private int _scalePercent = ProgramConfig.DefaultConfigurationFontScalePercent;

    public ConfigurationSettingsTree()
    {
        BackColor = ConfigurationTheme.Canvas;
        _content = new FlowLayoutPanel
        {
            AutoScroll = true,
            BackColor = ConfigurationTheme.Canvas,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(12, 12, 12, 24),
            WrapContents = false,
        };
        _content.Resize += (_, _) => UpdateControlWidths();
        Controls.Add(_content);
    }

    public void Bind(
        object view,
        int scalePercent,
        Action valueChanged,
        Action openAdvanced,
        IReadOnlyList<ConfigurationCategoryAction>? actions = null)
    {
        if (!ReferenceEquals(_view, view) || _searchResults is not null)
        {
            _expandedGroupKeys.Clear();
            _expansionStateInitialized = false;
        }

        _view = view;
        _searchResults = null;
        _searchQuery = string.Empty;
        _scalePercent = Math.Clamp(
            scalePercent,
            ProgramConfig.MinConfigurationFontScalePercent,
            ProgramConfig.MaxConfigurationFontScalePercent);
        _valueChanged = valueChanged;
        _openAdvanced = openAdvanced;
        _actions = actions;
        Rebuild();
    }

    public void BindSearch(
        IReadOnlyList<ConfigurationSearchResult> results,
        string query,
        int scalePercent,
        Action valueChanged,
        Action openAdvanced)
    {
        if (!string.Equals(_searchQuery, query, StringComparison.CurrentCultureIgnoreCase))
        {
            _expandedGroupKeys.Clear();
            _expansionStateInitialized = false;
        }

        _searchResults = results;
        _searchQuery = query;
        _scalePercent = Math.Clamp(
            scalePercent,
            ProgramConfig.MinConfigurationFontScalePercent,
            ProgramConfig.MaxConfigurationFontScalePercent);
        _valueChanged = valueChanged;
        _openAdvanced = openAdvanced;
        _actions = null;
        Rebuild();
    }

    public void SetScale(int scalePercent)
    {
        var clamped = Math.Clamp(
            scalePercent,
            ProgramConfig.MinConfigurationFontScalePercent,
            ProgramConfig.MaxConfigurationFontScalePercent);
        if (_scalePercent == clamped)
            return;

        _scalePercent = clamped;
        if (_view is not null || _searchResults is not null)
            Rebuild();
    }

    private void Rebuild()
    {
        if (_view is null && _searchResults is null)
            return;

        _content.SuspendLayout();
        var scrollOffset = Math.Max(0, -_content.AutoScrollPosition.Y);
        try
        {
            var oldControls = _content.Controls.Cast<Control>().ToArray();
            _content.Controls.Clear();
            foreach (var control in oldControls)
                control.Dispose();

            if (_searchResults is not null)
            {
                BuildSearchResults(_searchResults);
                _expansionStateInitialized = true;
                return;
            }

            if (_view is null)
                return;

            var properties = TypeDescriptor.GetProperties(_view)
                .Cast<PropertyDescriptor>()
                .Where(property => property.IsBrowsable)
                .ToArray();

            var groups = properties
                .GroupBy(property => Humanize(property.Category))
                .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            for (var index = 0; index < groups.Length; index++)
            {
                var propertyGroup = groups[index];
                var groupKey = $"category:{propertyGroup.Key}";
                var group = CreateDisclosureGroup(
                    groupKey,
                    propertyGroup.Key,
                    $"{propertyGroup.Count()} setting{(propertyGroup.Count() == 1 ? string.Empty : "s")}",
                    depth: 0,
                    expandedByDefault: index == 0);

                foreach (var property in propertyGroup)
                    AddPropertyControl(group, _view, property, depth: 1, groupKey);

                _content.Controls.Add(group);
            }

            AddCategoryActions();
            _expansionStateInitialized = true;
        }
        finally
        {
            _content.ResumeLayout(true);
            UpdateControlWidths();
            _content.AutoScrollPosition = new Point(0, scrollOffset);
        }
    }

    private void BuildSearchResults(IReadOnlyList<ConfigurationSearchResult> results)
    {
        if (results.Count == 0)
        {
            _content.Controls.Add(new ConfigurationSearchEmptyState(_searchQuery, _scalePercent));
            return;
        }

        var groups = results
            .GroupBy(result => result.Entry.CategoryName)
            .OrderByDescending(group => group.Max(result => result.Score))
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase);
        foreach (var resultGroup in groups)
        {
            var groupKey = $"search:{resultGroup.Key}";
            var resultArray = resultGroup
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            var group = CreateDisclosureGroup(
                groupKey,
                resultGroup.Key,
                $"{resultArray.Length} match{(resultArray.Length == 1 ? string.Empty : "es")}",
                depth: 0,
                expandedByDefault: true);

            foreach (var result in resultArray)
            {
                var entry = result.Entry;
                object? value;
                try
                {
                    value = entry.Property.GetValue(entry.Owner);
                }
                catch
                {
                    value = null;
                }

                var editor = CreateEditor(entry.Owner, entry.Property, value);
                ConfigureEditorAccessibility(editor, entry.Title, entry.Description);
                var description = string.IsNullOrWhiteSpace(entry.Description)
                    ? entry.Breadcrumb
                    : string.IsNullOrWhiteSpace(entry.Breadcrumb)
                        ? entry.Description
                        : $"{entry.Breadcrumb} \u00b7 {entry.Description}";
                group.AddChild(new ModernSettingRow(
                    entry.Title,
                    description,
                    editor,
                    _scalePercent,
                    depth: 1));
            }

            _content.Controls.Add(group);
        }
    }

    private void AddPropertyControl(
        ModernDisclosureGroup parent,
        object component,
        PropertyDescriptor property,
        int depth,
        string parentKey)
    {
        var owner = GetPropertyOwner(component, property);
        object? value;
        try
        {
            value = property.GetValue(owner);
        }
        catch
        {
            value = null;
        }

        if (depth <= 4 && value is not null && property.Converter.GetPropertiesSupported())
        {
            var nestedProperties = property.Converter
                .GetProperties(null, value, null)?
                .Cast<PropertyDescriptor>()
                .Where(nested => nested.IsBrowsable)
                .ToArray() ?? [];

            if (nestedProperties.Length > 0)
            {
                var groupKey = $"{parentKey}/{property.Name}";
                var nestedGroup = CreateDisclosureGroup(
                    groupKey,
                    Humanize(property.DisplayName),
                    GetSummary(value),
                    depth,
                    expandedByDefault: false);
                foreach (var nestedProperty in nestedProperties)
                    AddPropertyControl(nestedGroup, value, nestedProperty, depth + 1, groupKey);
                parent.AddChild(nestedGroup);
                return;
            }
        }

        var title = Humanize(property.DisplayName);
        var editor = CreateEditor(owner, property, value);
        ConfigureEditorAccessibility(editor, title, property.Description);
        var row = new ModernSettingRow(
            title,
            property.Description,
            editor,
            _scalePercent,
            depth);
        parent.AddChild(row);
    }

    private void AddCategoryActions()
    {
        if (_actions is not { Count: > 0 })
            return;

        const string groupKey = "category:test-and-maintenance";
        var group = CreateDisclosureGroup(
            groupKey,
            "Restart test",
            $"{_actions.Count} action{(_actions.Count == 1 ? string.Empty : "s")}",
            depth: 0,
            expandedByDefault: true);
        foreach (var action in _actions)
        {
            var editor = new ModernActionButton(
                action.ButtonText,
                action.ExecuteAsync,
                _scalePercent);
            ConfigureEditorAccessibility(editor, action.Title, action.Description);
            group.AddChild(new ModernSettingRow(
                action.Title,
                action.Description,
                editor,
                _scalePercent,
                depth: 1));
        }

        _content.Controls.Add(group);
    }

    private Control CreateEditor(object owner, PropertyDescriptor property, object? value)
    {
        if (property.Attributes[typeof(CurrentSystemTimeAttribute)] is CurrentSystemTimeAttribute)
            return new ModernSystemTimeDisplay(_scalePercent);

        if (property.Attributes[typeof(RestartTimePickerAttribute)] is RestartTimePickerAttribute &&
            value is string restartSchedule)
        {
            return new ModernRestartTimePickerEditor(
                restartSchedule,
                cron =>
                {
                    property.SetValue(owner, cron);
                    _valueChanged?.Invoke();
                },
                _scalePercent);
        }

        if (ConfigurationCollectionEditor.CanEdit(property, value))
        {
            return new ModernAdvancedValueButton(
                GetSummary(value),
                () => ConfigurationCollectionEditor.Edit(
                    this,
                    owner,
                    property,
                    value,
                    () =>
                    {
                        _valueChanged?.Invoke();
                        RequestRebuild();
                    }),
                _scalePercent,
                "Edit list…");
        }

        if (!property.IsReadOnly && property.Converter.GetStandardValuesSupported())
        {
            var values = property.Converter.GetStandardValues()?.Cast<object>().ToArray() ?? [];
            if (values.Length > 0)
            {
                return new ModernChoiceEditor(
                    values,
                    value,
                    option => property.Converter.ConvertToString(null, CultureInfo.CurrentCulture, option) ??
                              option?.ToString() ??
                              string.Empty,
                    selected =>
                    {
                        property.SetValue(owner, selected);
                        _valueChanged?.Invoke();
                    },
                    _scalePercent);
            }
        }

        if (!property.IsReadOnly &&
            property.Converter.CanConvertFrom(typeof(string)) &&
            property.Converter.CanConvertTo(typeof(string)) &&
            (value is not IEnumerable || value is string))
        {
            var password =
                property.Attributes[typeof(PasswordPropertyTextAttribute)] is PasswordPropertyTextAttribute
                {
                    Password: true,
                };
            return new ModernTextValueEditor(
                property.Converter.ConvertToString(null, CultureInfo.CurrentCulture, value) ?? string.Empty,
                text => property.Converter.ConvertFromString(null, CultureInfo.CurrentCulture, text),
                converted =>
                {
                    property.SetValue(owner, converted);
                    _valueChanged?.Invoke();
                },
                password,
                _scalePercent);
        }

        return new ModernAdvancedValueButton(GetSummary(value), _openAdvanced, _scalePercent);
    }

    private void RequestRebuild()
    {
        if (_rebuildPending || IsDisposed)
            return;

        if (!IsHandleCreated)
        {
            Rebuild();
            return;
        }

        _rebuildPending = true;
        BeginInvoke((Action)(() =>
        {
            _rebuildPending = false;
            if (!IsDisposed)
                Rebuild();
        }));
    }

    private ModernDisclosureGroup CreateDisclosureGroup(
        string key,
        string title,
        string subtitle,
        int depth,
        bool expandedByDefault)
    {
        var expanded = _expansionStateInitialized
            ? _expandedGroupKeys.Contains(key)
            : expandedByDefault;
        var group = new ModernDisclosureGroup(title, subtitle, _scalePercent, depth, expanded);
        if (expanded)
            _expandedGroupKeys.Add(key);
        group.ExpandedChanged += (_, _) =>
        {
            if (group.Expanded)
                _expandedGroupKeys.Add(key);
            else
                _expandedGroupKeys.Remove(key);
        };
        return group;
    }

    private static void ConfigureEditorAccessibility(Control editor, string title, string description)
    {
        switch (editor)
        {
            case ModernChoiceEditor choice:
                choice.SetAccessibility(title, description);
                break;
            case ModernTextValueEditor text:
                text.SetAccessibility(title, description);
                break;
            default:
                var action = editor.AccessibleName;
                editor.AccessibleName = string.IsNullOrWhiteSpace(action)
                    ? title
                    : $"{title}. {action}";
                editor.AccessibleDescription = description;
                break;
        }
    }

    private void UpdateControlWidths()
    {
        var availableWidth = Math.Max(
            300,
            _content.ClientSize.Width -
            _content.Padding.Horizontal -
            SystemInformation.VerticalScrollBarWidth -
            4);

        foreach (Control control in _content.Controls)
        {
            if (control is ModernDisclosureGroup group)
                group.SetAvailableWidth(availableWidth);
            else
                control.Width = availableWidth;
        }
    }

    private static object GetPropertyOwner(object component, PropertyDescriptor property)
    {
        if (component is ICustomTypeDescriptor descriptor)
            return descriptor.GetPropertyOwner(property) ?? component;
        return component;
    }

    private static string GetSummary(object? value)
    {
        if (value is null)
            return "Not set";
        if (value is ICollection collection)
            return $"{collection.Count} item{(collection.Count == 1 ? string.Empty : "s")}";

        var summary = value.ToString();
        if (string.IsNullOrWhiteSpace(summary) || summary == value.GetType().FullName)
            return "Expand for options";
        return summary;
    }

    private static string Humanize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Other";

        var spaced = Regex.Replace(text, "(?<=[a-z0-9])(?=[A-Z])", " ");
        spaced = spaced.Replace('_', ' ').Trim();
        return spaced.Length == 0
            ? "Other"
            : char.ToUpper(spaced[0], CultureInfo.CurrentCulture) + spaced[1..];
    }
}

internal sealed class ModernDisclosureGroup : FlowLayoutPanel
{
    private const int Indent = 18;
    private readonly ModernDisclosureHeader _header;
    private readonly FlowLayoutPanel _children;
    private readonly int _depth;

    public ModernDisclosureGroup(
        string title,
        string subtitle,
        int scalePercent,
        int depth,
        bool expanded)
    {
        _depth = depth;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.Transparent;
        FlowDirection = FlowDirection.TopDown;
        Margin = new Padding(depth == 0 ? 0 : Indent, 0, 0, 8);
        WrapContents = false;

        _header = new ModernDisclosureHeader(title, subtitle, scalePercent, depth, expanded);
        _children = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(0),
            Visible = expanded,
            WrapContents = false,
        };
        _children.Paint += Children_Paint;
        _header.ExpandedChanged += (_, _) =>
        {
            _children.Visible = _header.Expanded;
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        };

        Controls.Add(_header);
        Controls.Add(_children);
    }

    public event EventHandler? ExpandedChanged;
    public bool Expanded => _header.Expanded;

    public void AddChild(Control control) => _children.Controls.Add(control);

    public void SetAvailableWidth(int availableWidth)
    {
        var width = Math.Max(260, availableWidth - (_depth == 0 ? 0 : Indent));
        Width = width;
        _header.Width = width;
        _children.Width = width;

        foreach (Control child in _children.Controls)
        {
            if (child is ModernDisclosureGroup nested)
                nested.SetAvailableWidth(width - Indent);
            else
                child.Width = Math.Max(220, width - Indent);
        }
    }

    private void Children_Paint(object? sender, PaintEventArgs e)
    {
        using var guide = new Pen(ConfigurationTheme.Border, 1);
        e.Graphics.DrawLine(guide, 8, 0, 8, _children.Height);
    }
}

internal sealed class ModernDisclosureHeader : Control
{
    private readonly Font _titleFont;
    private readonly Font _subtitleFont;
    private readonly int _scalePercent;
    private readonly int _depth;
    private bool _hot;

    public ModernDisclosureHeader(
        string title,
        string subtitle,
        int scalePercent,
        int depth,
        bool expanded)
    {
        Title = title;
        Subtitle = subtitle;
        _scalePercent = scalePercent;
        _depth = depth;
        Expanded = expanded;
        AccessibleName = $"{title}, {(expanded ? "expanded" : "collapsed")}";
        AccessibleRole = AccessibleRole.PushButton;
        BackColor = ConfigurationTheme.Canvas;
        Cursor = Cursors.Hand;
        Height = ConfigurationTheme.ScalePixels(depth == 0 ? 48 : 44, scalePercent);
        TabStop = true;
        _titleFont = new Font(
            "Segoe UI Semibold",
            ConfigurationTheme.ScaleFont(depth == 0 ? 10.5F : 10F, scalePercent));
        _subtitleFont = new Font(
            "Segoe UI",
            ConfigurationTheme.ScaleFont(8.5F, scalePercent));
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    public event EventHandler? ExpandedChanged;
    public string Title { get; }
    public string Subtitle { get; }
    public bool Expanded { get; private set; }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        ToggleExpanded();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            ToggleExpanded();
        }
        else if (e.KeyCode == Keys.Right && !Expanded)
        {
            e.Handled = true;
            ToggleExpanded();
        }
        else if (e.KeyCode == Keys.Left && Expanded)
        {
            e.Handled = true;
            ToggleExpanded();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        var background = Expanded
            ? ConfigurationTheme.SurfaceSelected
            : _hot
                ? ConfigurationTheme.SurfaceHover
                : ConfigurationTheme.SurfaceRaised;

        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = new GraphicsPath())
        using (var fill = new SolidBrush(background))
        using (var border = new Pen(Expanded ? ConfigurationTheme.Accent : ConfigurationTheme.Border))
        {
            path.AddRoundedRectangle(bounds, 8);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        if (Expanded)
        {
            using var accent = new SolidBrush(ConfigurationTheme.Accent);
            e.Graphics.FillRectangle(
                accent,
                bounds.Left,
                bounds.Top + 7,
                3,
                bounds.Height - 14);
        }

        var chevronX = ConfigurationTheme.ScalePixels(17, _scalePercent);
        var centerY = Height / 2;
        var chevronSize = ConfigurationTheme.ScalePixels(5, _scalePercent);
        using (var chevron = new Pen(
                   Expanded ? ConfigurationTheme.Accent : ConfigurationTheme.TextMuted,
                   ConfigurationTheme.ScalePixels(2, _scalePercent)))
        {
            chevron.StartCap = LineCap.Round;
            chevron.EndCap = LineCap.Round;
            if (Expanded)
            {
                e.Graphics.DrawLines(
                    chevron,
                    [
                        new Point(chevronX - chevronSize, centerY - 2),
                        new Point(chevronX, centerY + chevronSize - 2),
                        new Point(chevronX + chevronSize, centerY - 2),
                    ]);
            }
            else
            {
                e.Graphics.DrawLines(
                    chevron,
                    [
                        new Point(chevronX - 2, centerY - chevronSize),
                        new Point(chevronX + chevronSize - 2, centerY),
                        new Point(chevronX - 2, centerY + chevronSize),
                    ]);
            }
        }

        var titleLeft = ConfigurationTheme.ScalePixels(38, _scalePercent);
        var subtitleWidth = TextRenderer.MeasureText(Subtitle, _subtitleFont).Width;
        var subtitleBounds = new Rectangle(
            Math.Max(titleLeft, Width - subtitleWidth - ConfigurationTheme.ScalePixels(14, _scalePercent)),
            0,
            subtitleWidth,
            Height);
        TextRenderer.DrawText(
            e.Graphics,
            Subtitle,
            _subtitleFont,
            subtitleBounds,
            ConfigurationTheme.TextMuted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var titleBounds = new Rectangle(
            titleLeft,
            0,
            Math.Max(20, subtitleBounds.Left - titleLeft - 12),
            Height);
        TextRenderer.DrawText(
            e.Graphics,
            Title,
            _titleFont,
            titleBounds,
            ConfigurationTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _subtitleFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ToggleExpanded()
    {
        Expanded = !Expanded;
        AccessibleName = $"{Title}, {(Expanded ? "expanded" : "collapsed")}";
        Invalidate();
        ExpandedChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class ModernSettingRow : Panel
{
    private readonly Label _title;
    private readonly Label _description;
    private readonly Control _editor;
    private readonly int _scalePercent;
    private bool _hot;

    public ModernSettingRow(
        string title,
        string description,
        Control editor,
        int scalePercent,
        int depth)
    {
        _scalePercent = scalePercent;
        _editor = editor;
        AccessibleName = title;
        BackColor = Color.Transparent;
        Height = ConfigurationTheme.ScalePixels(58, scalePercent);
        Margin = new Padding(depth > 0 ? 18 : 0, 0, 0, 4);

        _title = new Label
        {
            AutoEllipsis = true,
            Font = new Font(
                "Segoe UI Semibold",
                ConfigurationTheme.ScaleFont(9.5F, scalePercent)),
            ForeColor = ConfigurationTheme.TextPrimary,
            Text = title,
        };
        _description = new Label
        {
            AutoEllipsis = true,
            Font = new Font(
                "Segoe UI",
                ConfigurationTheme.ScaleFont(8F, scalePercent)),
            ForeColor = ConfigurationTheme.TextMuted,
            Text = description,
        };

        Controls.Add(_title);
        Controls.Add(_description);
        Controls.Add(_editor);
        WireHover(this);
        Resize += (_, _) => LayoutChildren();
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hot)
        {
            using var path = new GraphicsPath();
            path.AddRoundedRectangle(Rectangle.Inflate(ClientRectangle, -1, -1), 7);
            using var fill = new SolidBrush(ConfigurationTheme.SurfaceHover);
            e.Graphics.FillPath(fill, path);
        }

        var dotSize = ConfigurationTheme.ScalePixels(4, _scalePercent);
        using var dot = new SolidBrush(_hot ? ConfigurationTheme.Accent : ConfigurationTheme.TextMuted);
        e.Graphics.FillEllipse(
            dot,
            ConfigurationTheme.ScalePixels(12, _scalePercent),
            (Height - dotSize) / 2,
            dotSize,
            dotSize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _title.Font.Dispose();
            _description.Font.Dispose();
        }
        base.Dispose(disposing);
    }

    private void LayoutChildren()
    {
        var outerPadding = ConfigurationTheme.ScalePixels(12, _scalePercent);
        var editorWidth = Math.Clamp(
            (int)Math.Round(Width * 0.36F),
            ConfigurationTheme.ScalePixels(170, _scalePercent),
            ConfigurationTheme.ScalePixels(250, _scalePercent));
        var editorHeight = ConfigurationTheme.ScalePixels(34, _scalePercent);
        _editor.Bounds = new Rectangle(
            Width - editorWidth - outerPadding,
            (Height - editorHeight) / 2,
            editorWidth,
            editorHeight);

        var textLeft = ConfigurationTheme.ScalePixels(28, _scalePercent);
        var textWidth = Math.Max(40, _editor.Left - textLeft - outerPadding);
        var hasDescription = !string.IsNullOrWhiteSpace(_description.Text);
        _title.Bounds = new Rectangle(
            textLeft,
            hasDescription ? ConfigurationTheme.ScalePixels(8, _scalePercent) : 0,
            textWidth,
            hasDescription
                ? ConfigurationTheme.ScalePixels(22, _scalePercent)
                : Height);
        _title.TextAlign = hasDescription
            ? ContentAlignment.MiddleLeft
            : ContentAlignment.MiddleLeft;
        _description.Bounds = new Rectangle(
            textLeft,
            ConfigurationTheme.ScalePixels(30, _scalePercent),
            textWidth,
            ConfigurationTheme.ScalePixels(18, _scalePercent));
    }

    private void WireHover(Control control)
    {
        control.MouseEnter += (_, _) =>
        {
            _hot = true;
            Invalidate();
        };
        control.MouseLeave += (_, _) =>
        {
            if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
            {
                _hot = false;
                Invalidate();
            }
        };

        foreach (Control child in control.Controls)
            WireHover(child);
    }
}

internal sealed class ModernChoiceEditor : Button
{
    private readonly object[] _values;
    private readonly Func<object?, string> _display;
    private readonly Action<object> _valueChanged;
    private readonly Font _ownedFont;
    private readonly int _scalePercent;
    private ContextMenuStrip? _activeMenu;
    private string _accessibleLabel = "Setting";
    private object? _value;
    private bool _disposing;
    private bool _hot;

    public ModernChoiceEditor(
        object[] values,
        object? value,
        Func<object?, string> display,
        Action<object> valueChanged,
        int scalePercent)
    {
        _values = values;
        _value = value;
        _display = display;
        _valueChanged = valueChanged;
        _scalePercent = scalePercent;
        _ownedFont = new Font(
            "Segoe UI Semibold",
            ConfigurationTheme.ScaleFont(9F, scalePercent));
        AccessibleRole = AccessibleRole.ComboBox;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    public void SetAccessibility(string label, string description)
    {
        _accessibleLabel = label;
        AccessibleDescription = description;
        UpdateAccessibleName();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        ShowOptions();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = new GraphicsPath())
        using (var fill = new SolidBrush(_hot ? ConfigurationTheme.SurfaceHover : ConfigurationTheme.Surface))
        using (var border = new Pen(Focused ? ConfigurationTheme.Accent : ConfigurationTheme.BorderStrong))
        {
            path.AddRoundedRectangle(bounds, 6);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        var chevronRight = Width - ConfigurationTheme.ScalePixels(14, _scalePercent);
        var centerY = Height / 2;
        using (var chevron = new Pen(ConfigurationTheme.TextMuted, 2))
        {
            chevron.StartCap = LineCap.Round;
            chevron.EndCap = LineCap.Round;
            e.Graphics.DrawLines(
                chevron,
                [
                    new Point(chevronRight - 5, centerY - 2),
                    new Point(chevronRight, centerY + 3),
                    new Point(chevronRight + 5, centerY - 2),
                ]);
        }

        TextRenderer.DrawText(
            e.Graphics,
            _display(_value),
            _ownedFont,
            new Rectangle(
                ConfigurationTheme.ScalePixels(11, _scalePercent),
                0,
                Width - ConfigurationTheme.ScalePixels(38, _scalePercent),
                Height),
            ConfigurationTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposing = true;
            var menu = _activeMenu;
            _activeMenu = null;
            if (menu is not null && !menu.IsDisposed)
                menu.Dispose();
            _ownedFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void ShowOptions()
    {
        if (_disposing || IsDisposed)
            return;

        if (_activeMenu is { IsDisposed: false, Visible: true })
            return;

        var previousMenu = _activeMenu;
        _activeMenu = null;
        if (previousMenu is not null && !previousMenu.IsDisposed)
            previousMenu.Dispose();

        var itemHeight = ConfigurationTheme.ScalePixels(36, _scalePercent);
        var menuWidth = Math.Max(Width, ConfigurationTheme.ScalePixels(160, _scalePercent));
        var menu = new ContextMenuStrip
        {
            AutoSize = true,
            BackColor = ConfigurationTheme.SurfaceRaised,
            ForeColor = ConfigurationTheme.TextPrimary,
            MinimumSize = new Size(menuWidth, 0),
            Padding = new Padding(1),
            Renderer = new ConfigurationMenuRenderer(),
            ShowCheckMargin = false,
            ShowImageMargin = false,
        };

        foreach (var option in _values)
        {
            var selected = Equals(option, _value);
            var item = new ToolStripMenuItem($"{(selected ? "✓  " : "    ")}{_display(option)}")
            {
                AutoSize = false,
                BackColor = selected ? ConfigurationTheme.SurfaceSelected : ConfigurationTheme.SurfaceRaised,
                ForeColor = ConfigurationTheme.TextPrimary,
                Padding = new Padding(10, 6, 10, 6),
                Size = new Size(menuWidth - menu.Padding.Horizontal, itemHeight),
                Tag = option,
            };
            item.Click += (_, _) =>
            {
                _value = item.Tag;
                if (_value is not null)
                    _valueChanged(_value);
                UpdateAccessibleName();
                Invalidate();
            };
            menu.Items.Add(item);
        }

        menu.Opening += (_, _) =>
        {
            var availableWidth = Math.Max(1, menu.ClientSize.Width - menu.Padding.Horizontal);
            foreach (ToolStripItem item in menu.Items)
                item.Size = new Size(availableWidth, itemHeight);
        };
        menu.Closed += (_, _) =>
        {
            if (_disposing || IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (ReferenceEquals(_activeMenu, menu))
                        _activeMenu = null;
                    if (!menu.IsDisposed)
                        menu.Dispose();
                }));
            }
            catch (ObjectDisposedException)
            {
                // The editor owns the menu and disposes it with the control.
            }
            catch (InvalidOperationException)
            {
                // The editor handle was destroyed while the menu was closing.
            }
        };
        _activeMenu = menu;
        menu.Show(this, new Point(0, Height));
    }

    private void UpdateAccessibleName() =>
        AccessibleName = $"{_accessibleLabel}, current value {_display(_value)}";
}

internal sealed class ConfigurationSearchEmptyState : Control
{
    private readonly Font _titleFont;
    private readonly Font _descriptionFont;
    private readonly string _query;

    public ConfigurationSearchEmptyState(string query, int scalePercent)
    {
        _query = query;
        AccessibleName = $"No settings found for {query}";
        BackColor = ConfigurationTheme.Canvas;
        Height = ConfigurationTheme.ScalePixels(116, scalePercent);
        Margin = new Padding(0, 4, 0, 0);
        _titleFont = new Font(
            "Segoe UI Semibold",
            ConfigurationTheme.ScaleFont(10.5F, scalePercent));
        _descriptionFont = new Font(
            "Segoe UI",
            ConfigurationTheme.ScaleFont(8.5F, scalePercent));
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = new GraphicsPath())
        using (var fill = new SolidBrush(ConfigurationTheme.SurfaceRaised))
        using (var border = new Pen(ConfigurationTheme.Border))
        {
            path.AddRoundedRectangle(bounds, 8);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            "No matching settings",
            _titleFont,
            new Rectangle(20, 24, Math.Max(40, Width - 40), 24),
            ConfigurationTheme.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(
            e.Graphics,
            $"Try a setting name, category, or related phrase instead of \u201c{_query}\u201d.",
            _descriptionFont,
            new Rectangle(20, 54, Math.Max(40, Width - 40), 38),
            ConfigurationTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont.Dispose();
            _descriptionFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class ModernTextValueEditor : UserControl
{
    private readonly TextBox _textBox;
    private readonly Func<string, object?> _convert;
    private readonly Action<object?> _valueChanged;
    private readonly Font _ownedFont;
    private string _committedValue;
    private bool _invalid;

    public ModernTextValueEditor(
        string value,
        Func<string, object?> convert,
        Action<object?> valueChanged,
        bool password,
        int scalePercent)
    {
        _committedValue = value;
        _convert = convert;
        _valueChanged = valueChanged;
        _ownedFont = new Font(
            "Segoe UI",
            ConfigurationTheme.ScaleFont(9F, scalePercent));
        BackColor = ConfigurationTheme.Surface;
        _textBox = new TextBox
        {
            BackColor = ConfigurationTheme.Surface,
            BorderStyle = BorderStyle.None,
            Font = _ownedFont,
            ForeColor = ConfigurationTheme.TextPrimary,
            Text = value,
            UseSystemPasswordChar = password,
        };
        _textBox.KeyDown += TextBox_KeyDown;
        _textBox.Leave += (_, _) => Commit();
        Controls.Add(_textBox);
        Resize += (_, _) => LayoutTextBox();
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void SetAccessibility(string label, string description)
    {
        AccessibleName = label;
        AccessibleDescription = description;
        _textBox.AccessibleName = label;
        _textBox.AccessibleDescription = description;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        path.AddRoundedRectangle(Rectangle.Inflate(ClientRectangle, -1, -1), 6);
        using var border = new Pen(
            _invalid
                ? ConfigurationTheme.Accent
                : _textBox.Focused
                    ? ConfigurationTheme.Accent
                    : ConfigurationTheme.BorderStrong);
        e.Graphics.DrawPath(border, path);
    }

    private void LayoutTextBox()
    {
        _textBox.Bounds = new Rectangle(
            10,
            Math.Max(1, (Height - _textBox.PreferredHeight) / 2),
            Math.Max(20, Width - 20),
            _textBox.PreferredHeight);
    }

    private void TextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            Commit();
            Parent?.Focus();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            _textBox.Text = _committedValue;
            _invalid = false;
            Invalidate();
            Parent?.Focus();
        }
    }

    private void Commit()
    {
        if (_textBox.Text == _committedValue)
        {
            _invalid = false;
            Invalidate();
            return;
        }

        try
        {
            var converted = _convert(_textBox.Text);
            _valueChanged(converted);
            _committedValue = _textBox.Text;
            _invalid = false;
        }
        catch
        {
            _invalid = true;
        }
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ownedFont.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class ModernAdvancedValueButton : Button
{
    private readonly Font _ownedFont;

    public ModernAdvancedValueButton(
        string summary,
        Action? openAdvanced,
        int scalePercent,
        string actionLabel = "Advanced…")
    {
        AccessibleName = $"Open advanced editor. Current value: {summary}";
        BackColor = ConfigurationTheme.Surface;
        Cursor = Cursors.Hand;
        FlatAppearance.BorderColor = ConfigurationTheme.BorderStrong;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseDownBackColor = ConfigurationTheme.SurfaceSelected;
        FlatAppearance.MouseOverBackColor = ConfigurationTheme.SurfaceHover;
        FlatStyle = FlatStyle.Flat;
        _ownedFont = new Font(
            "Segoe UI Semibold",
            ConfigurationTheme.ScaleFont(8.5F, scalePercent));
        Font = _ownedFont;
        ForeColor = ConfigurationTheme.TextSecondary;
        Text = $"{summary}  ·  {actionLabel}";
        TextAlign = ContentAlignment.MiddleLeft;
        Click += (_, _) => openAdvanced?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ownedFont.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class ModernActionButton : Button
{
    private readonly Func<Task> _executeAsync;
    private readonly Font _ownedFont;
    private readonly string _idleText;
    private bool _running;

    public ModernActionButton(
        string text,
        Func<Task> executeAsync,
        int scalePercent)
    {
        _idleText = text;
        _executeAsync = executeAsync;
        AccessibleRole = AccessibleRole.PushButton;
        BackColor = ConfigurationTheme.Surface;
        Cursor = Cursors.Hand;
        FlatAppearance.BorderColor = ConfigurationTheme.Accent;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseDownBackColor = ConfigurationTheme.AccentPressed;
        FlatAppearance.MouseOverBackColor = ConfigurationTheme.SurfaceSelected;
        FlatStyle = FlatStyle.Flat;
        _ownedFont = new Font(
            "Segoe UI Semibold",
            ConfigurationTheme.ScaleFont(8.5F, scalePercent));
        Font = _ownedFont;
        ForeColor = ConfigurationTheme.TextPrimary;
        Text = text;
        Click += Execute_Click;
    }

    private async void Execute_Click(object? sender, EventArgs e)
    {
        if (_running)
            return;

        _running = true;
        Enabled = false;
        Text = "Restarting\u2026";
        try
        {
            await _executeAsync();
        }
        finally
        {
            if (!IsDisposed)
            {
                Text = _idleText;
                Enabled = true;
                _running = false;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ownedFont.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class ModernRestartTimePickerEditor : UserControl
{
    private readonly ModernChoiceEditor _hourPicker;
    private readonly ModernChoiceEditor _minutePicker;
    private readonly ModernChoiceEditor? _periodPicker;
    private readonly Action<string> _valueChanged;
    private readonly bool _use24HourClock;
    private int _hour;
    private int _minute;
    private bool _isPm;

    public ModernRestartTimePickerEditor(
        string cron,
        Action<string> valueChanged,
        int scalePercent)
    {
        _valueChanged = valueChanged;
        _use24HourClock = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H');
        BackColor = Color.Transparent;

        if (!CronSchedule.TryGetDailyTime(cron, out var restartTime))
        {
            try
            {
                restartTime = CronSchedule
                    .Parse(cron)
                    .GetNextOccurrence(DateTime.Now)
                    .TimeOfDay;
            }
            catch
            {
                restartTime = TimeSpan.FromHours(4);
            }
        }

        _hour = _use24HourClock ? restartTime.Hours : restartTime.Hours % 12;
        if (!_use24HourClock && _hour == 0)
            _hour = 12;
        _minute = restartTime.Minutes;
        _isPm = restartTime.Hours >= 12;

        _hourPicker = new ModernChoiceEditor(
            (_use24HourClock
                ? Enumerable.Range(0, 24)
                : Enumerable.Range(1, 12))
            .Cast<object>()
            .ToArray(),
            _hour,
            value => $"{value:00}",
            value =>
            {
                _hour = (int)value;
                Commit();
            },
            scalePercent);
        var minuteValues = Enumerable.Range(0, 12)
            .Select(index => index * 5)
            .Append(_minute)
            .Distinct()
            .Order()
            .Cast<object>()
            .ToArray();
        _minutePicker = new ModernChoiceEditor(
            minuteValues,
            _minute,
            value => $"{value:00}",
            value =>
            {
                _minute = (int)value;
                Commit();
            },
            scalePercent);
        if (!_use24HourClock)
        {
            var format = CultureInfo.CurrentCulture.DateTimeFormat;
            _periodPicker = new ModernChoiceEditor(
                new object[] { false, true },
                _isPm,
                value => value is true ? format.PMDesignator : format.AMDesignator,
                value =>
                {
                    _isPm = (bool)value;
                    Commit();
                },
                scalePercent);
        }
        _hourPicker.SetAccessibility("Restart hour", "Choose the local restart hour.");
        _minutePicker.SetAccessibility("Restart minute", "Choose the local restart minute.");
        _periodPicker?.SetAccessibility("Restart period", "Choose AM or PM.");
        Controls.Add(_hourPicker);
        Controls.Add(_minutePicker);
        if (_periodPicker is not null)
            Controls.Add(_periodPicker);
        Resize += (_, _) => LayoutPickers();
        UpdateAccessibleName();
    }

    private void Commit()
    {
        var hour24 = Get24Hour();
        _valueChanged(CronSchedule.FromDailyTime(new TimeSpan(hour24, _minute, 0)));
        UpdateAccessibleName();
    }

    private void LayoutPickers()
    {
        var gap = 4;
        if (_periodPicker is null)
        {
            var segmentWidth = Math.Max(48, (Width - gap) / 2);
            _hourPicker.Bounds = new Rectangle(0, 0, segmentWidth, Height);
            _minutePicker.Bounds = new Rectangle(
                segmentWidth + gap,
                0,
                Math.Max(48, Width - segmentWidth - gap),
                Height);
            return;
        }

        var periodWidth = Math.Clamp(Width / 3, 64, 82);
        var numericWidth = Math.Max(48, (Width - periodWidth - (gap * 2)) / 2);
        _hourPicker.Bounds = new Rectangle(0, 0, numericWidth, Height);
        _minutePicker.Bounds = new Rectangle(numericWidth + gap, 0, numericWidth, Height);
        _periodPicker.Bounds = new Rectangle(
            (numericWidth * 2) + (gap * 2),
            0,
            Math.Max(48, Width - (numericWidth * 2) - (gap * 2)),
            Height);
    }

    private void UpdateAccessibleName()
    {
        var selected = DateTime.Today.AddHours(Get24Hour()).AddMinutes(_minute);
        AccessibleName = $"Daily restart time, {selected.ToString("t", CultureInfo.CurrentCulture)}";
    }

    private int Get24Hour() => _use24HourClock
        ? _hour
        : _hour % 12 + (_isPm ? 12 : 0);
}

internal sealed class ModernSystemTimeDisplay : Control
{
    private readonly Font _ownedFont;
    private readonly System.Windows.Forms.Timer _clockTimer;

    public ModernSystemTimeDisplay(int scalePercent)
    {
        BackColor = ConfigurationTheme.Surface;
        _ownedFont = new Font(
            "Segoe UI Semibold",
            ConfigurationTheme.ScaleFont(8.75F, scalePercent));
        _clockTimer = new System.Windows.Forms.Timer
        {
            Interval = 1_000,
            Enabled = true,
        };
        _clockTimer.Tick += (_, _) =>
        {
            UpdateAccessibleName();
            Invalidate();
        };
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        UpdateAccessibleName();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = new GraphicsPath())
        using (var fill = new SolidBrush(ConfigurationTheme.Surface))
        using (var border = new Pen(ConfigurationTheme.BorderStrong))
        {
            path.AddRoundedRectangle(Rectangle.Inflate(ClientRectangle, -1, -1), 6);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        TextRenderer.DrawText(
            e.Graphics,
            $"Local \u00b7 {DateTime.Now.ToString("T", CultureInfo.CurrentCulture)}",
            _ownedFont,
            Rectangle.Inflate(ClientRectangle, -10, 0),
            ConfigurationTheme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clockTimer.Stop();
            _clockTimer.Dispose();
            _ownedFont.Dispose();
        }
        base.Dispose(disposing);
    }

    private void UpdateAccessibleName() =>
        AccessibleName = $"Current local system time, {DateTime.Now.ToString("T", CultureInfo.CurrentCulture)}";
}

internal sealed class ConfigurationMenuRenderer : ToolStripProfessionalRenderer
{
    public ConfigurationMenuRenderer() : base(new ConfigurationMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = ConfigurationTheme.TextPrimary;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var background = e.Item.Selected
            ? ConfigurationTheme.SurfaceHover
            : e.Item.BackColor;
        using var fill = new SolidBrush(background);
        e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
    }
}

internal sealed class ConfigurationMenuColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => ConfigurationTheme.BorderStrong;
    public override Color MenuItemBorder => ConfigurationTheme.Border;
    public override Color MenuItemSelected => ConfigurationTheme.SurfaceHover;
    public override Color MenuItemSelectedGradientBegin => ConfigurationTheme.SurfaceHover;
    public override Color MenuItemSelectedGradientEnd => ConfigurationTheme.SurfaceHover;
    public override Color ToolStripDropDownBackground => ConfigurationTheme.SurfaceRaised;
}

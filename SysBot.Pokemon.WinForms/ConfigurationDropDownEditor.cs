using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Design;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace SysBot.Pokemon.WinForms;

internal static class ConfigurationDropDownTheme
{
    private static readonly object RegistrationLock = new();
    private static bool _registered;
    private static int _scalePercent = ProgramConfig.DefaultConfigurationFontScalePercent;

    public static int ScalePercent => Volatile.Read(ref _scalePercent);

    public static void SetScale(int percent) =>
        Volatile.Write(
            ref _scalePercent,
            Math.Clamp(
                percent,
                ProgramConfig.MinConfigurationFontScalePercent,
                ProgramConfig.MaxConfigurationFontScalePercent));

    public static void Register(Assembly settingsAssembly)
    {
        lock (RegistrationLock)
        {
            if (_registered)
                return;

            var editor = new EditorAttribute(typeof(DarkStandardValuesEditor), typeof(UITypeEditor));
            TypeDescriptor.AddAttributes(typeof(bool), editor);

            foreach (var enumType in settingsAssembly.GetTypes().Where(type => type.IsEnum))
                TypeDescriptor.AddAttributes(enumType, editor);

            _registered = true;
        }
    }
}

public sealed class DarkStandardValuesEditor : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) =>
        context?.PropertyDescriptor?.Converter.GetStandardValuesSupported(context) == true
            ? UITypeEditorEditStyle.DropDown
            : UITypeEditorEditStyle.None;

    public override object? EditValue(
        ITypeDescriptorContext? context,
        IServiceProvider provider,
        object? value)
    {
        if (context?.PropertyDescriptor is null ||
            provider.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService editorService)
        {
            return value;
        }

        var converter = context.PropertyDescriptor.Converter;
        var values = converter.GetStandardValues(context);
        if (values is null || values.Count == 0)
            return value;

        using var dropDown = new DarkStandardValuesDropDown(
            values,
            converter,
            context,
            value,
            editorService);
        editorService.DropDownControl(dropDown);

        return dropDown.Cancelled ? value : dropDown.SelectedValue ?? value;
    }
}

internal sealed class DarkStandardValuesDropDown : Panel
{
    private const int VisibleItemLimit = 8;

    private readonly DarkStandardValuesListBox _list;
    private readonly object? _initialValue;
    private readonly IWindowsFormsEditorService _editorService;

    public DarkStandardValuesDropDown(
        ICollection values,
        TypeConverter converter,
        ITypeDescriptorContext context,
        object? currentValue,
        IWindowsFormsEditorService editorService)
    {
        _initialValue = currentValue;
        _editorService = editorService;
        var scalePercent = ConfigurationDropDownTheme.ScalePercent;
        var itemHeight = ConfigurationTheme.ScalePixels(34, scalePercent);

        BackColor = ConfigurationTheme.BorderStrong;
        Padding = new Padding(1);

        var displayValues = values.Cast<object>().ToArray();
        _list = new DarkStandardValuesListBox(converter, context, scalePercent)
        {
            Dock = DockStyle.Fill,
            ItemHeight = itemHeight,
        };
        _list.Items.AddRange(displayValues);
        _list.SelectedItem = currentValue;
        _list.MouseUp += List_MouseUp;
        _list.KeyDown += List_KeyDown;
        Controls.Add(_list);
        DarkModeHelper.ApplyDarkModeToControlTree(_list);

        var longestLabel = displayValues
            .Select(value => converter.ConvertToString(context, null, value) ?? value?.ToString() ?? string.Empty)
            .Select(label => TextRenderer.MeasureText(label, _list.Font).Width)
            .DefaultIfEmpty(140)
            .Max();
        Width = Math.Clamp(
            longestLabel + ConfigurationTheme.ScalePixels(52, scalePercent),
            ConfigurationTheme.ScalePixels(190, scalePercent),
            ConfigurationTheme.ScalePixels(420, scalePercent));
        Height = Math.Min(displayValues.Length, VisibleItemLimit) * itemHeight + Padding.Vertical;
    }

    public bool Cancelled { get; private set; }

    public object? SelectedValue => _list.SelectedItem;

    private void List_MouseUp(object? sender, MouseEventArgs e)
    {
        var index = _list.IndexFromPoint(e.Location);
        if (index == ListBox.NoMatches)
            return;

        _list.SelectedIndex = index;
        _editorService.CloseDropDown();
    }

    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            _editorService.CloseDropDown();
        }
        else if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            Cancelled = true;
            _list.SelectedItem = _initialValue;
            _editorService.CloseDropDown();
        }
    }
}

internal sealed class DarkStandardValuesListBox : ListBox
{
    private readonly TypeConverter _converter;
    private readonly ITypeDescriptorContext _context;
    private readonly Font _ownedFont;
    private readonly int _scalePercent;
    private int _hotIndex = NoMatches;

    public DarkStandardValuesListBox(TypeConverter converter, ITypeDescriptorContext context, int scalePercent)
    {
        _converter = converter;
        _context = context;
        _scalePercent = scalePercent;
        _ownedFont = new Font(
            "Segoe UI Semibold",
            ConfigurationTheme.ScaleFont(9.5F, scalePercent),
            FontStyle.Regular,
            GraphicsUnit.Point);

        BackColor = ConfigurationTheme.SurfaceRaised;
        BorderStyle = BorderStyle.None;
        Cursor = Cursors.Hand;
        DrawMode = DrawMode.OwnerDrawFixed;
        Font = _ownedFont;
        ForeColor = ConfigurationTheme.TextSecondary;
        IntegralHeight = false;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var nextHotIndex = IndexFromPoint(e.Location);
        if (nextHotIndex == _hotIndex)
            return;

        _hotIndex = nextHotIndex;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hotIndex = NoMatches;
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
            return;

        var selected = (e.State & DrawItemState.Selected) != 0;
        var hot = e.Index == _hotIndex;
        var background = selected
            ? ConfigurationTheme.SurfaceSelected
            : hot
                ? ConfigurationTheme.SurfaceHover
                : BackColor;

        using (var backgroundBrush = new SolidBrush(background))
            e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        if (selected)
        {
            var checkLeft = e.Bounds.Left + ConfigurationTheme.ScalePixels(13, _scalePercent);
            var checkCenterY = e.Bounds.Top + e.Bounds.Height / 2;
            using var checkPen = new Pen(
                ConfigurationTheme.Accent,
                ConfigurationTheme.ScalePixels(2, _scalePercent))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawLines(
                checkPen,
                [
                    new Point(checkLeft, checkCenterY),
                    new Point(
                        checkLeft + ConfigurationTheme.ScalePixels(4, _scalePercent),
                        checkCenterY + ConfigurationTheme.ScalePixels(4, _scalePercent)),
                    new Point(
                        checkLeft + ConfigurationTheme.ScalePixels(11, _scalePercent),
                        checkCenterY - ConfigurationTheme.ScalePixels(5, _scalePercent)),
                ]);
            e.Graphics.SmoothingMode = SmoothingMode.Default;
        }

        var value = Items[e.Index];
        var label = _converter.ConvertToString(_context, null, value) ?? value?.ToString() ?? string.Empty;
        var textLeft = e.Bounds.Left + ConfigurationTheme.ScalePixels(38, _scalePercent);
        var textBounds = new Rectangle(
            textLeft,
            e.Bounds.Top,
            e.Bounds.Right - textLeft - ConfigurationTheme.ScalePixels(12, _scalePercent),
            e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            label,
            Font,
            textBounds,
            selected ? ConfigurationTheme.TextPrimary : ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ownedFont.Dispose();
        base.Dispose(disposing);
    }
}

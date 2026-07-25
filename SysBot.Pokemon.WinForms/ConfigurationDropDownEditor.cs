using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace SysBot.Pokemon.WinForms;

internal static class ConfigurationDropDownTheme
{
    private static readonly object RegistrationLock = new();
    private static bool _registered;

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
    private const int ItemHeight = 32;
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

        BackColor = Color.FromArgb(72, 84, 98);
        Padding = new Padding(1);

        var displayValues = values.Cast<object>().ToArray();
        _list = new DarkStandardValuesListBox(converter, context)
        {
            Dock = DockStyle.Fill,
            ItemHeight = ItemHeight,
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
        Width = Math.Clamp(longestLabel + 58, 190, 420);
        Height = Math.Min(displayValues.Length, VisibleItemLimit) * ItemHeight + Padding.Vertical;
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
    private readonly Font _ownedFont = new("Segoe UI Semibold", 10F);
    private int _hotIndex = NoMatches;

    public DarkStandardValuesListBox(TypeConverter converter, ITypeDescriptorContext context)
    {
        _converter = converter;
        _context = context;

        BackColor = Color.FromArgb(23, 29, 37);
        BorderStyle = BorderStyle.None;
        Cursor = Cursors.Hand;
        DrawMode = DrawMode.OwnerDrawFixed;
        Font = _ownedFont;
        ForeColor = Color.FromArgb(226, 231, 236);
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
            ? Color.FromArgb(67, 42, 45)
            : hot
                ? Color.FromArgb(37, 46, 58)
                : BackColor;

        using (var backgroundBrush = new SolidBrush(background))
            e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        if (selected)
        {
            using var accentBrush = new SolidBrush(Color.FromArgb(230, 77, 77));
            e.Graphics.FillRectangle(accentBrush, e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height);
            e.Graphics.FillEllipse(
                accentBrush,
                e.Bounds.Left + 13,
                e.Bounds.Top + (e.Bounds.Height - 8) / 2,
                8,
                8);
        }
        else
        {
            using var outline = new Pen(Color.FromArgb(112, 127, 143));
            e.Graphics.DrawEllipse(
                outline,
                e.Bounds.Left + 13,
                e.Bounds.Top + (e.Bounds.Height - 8) / 2,
                8,
                8);
        }

        var value = Items[e.Index];
        var label = _converter.ConvertToString(_context, null, value) ?? value?.ToString() ?? string.Empty;
        var textBounds = new Rectangle(
            e.Bounds.Left + 34,
            e.Bounds.Top,
            e.Bounds.Width - 42,
            e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            label,
            Font,
            textBounds,
            selected ? Color.FromArgb(244, 246, 248) : ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        using var divider = new Pen(Color.FromArgb(48, 60, 73));
        e.Graphics.DrawLine(
            divider,
            e.Bounds.Left + 8,
            e.Bounds.Bottom - 1,
            e.Bounds.Right - 8,
            e.Bounds.Bottom - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _ownedFont.Dispose();
        base.Dispose(disposing);
    }
}

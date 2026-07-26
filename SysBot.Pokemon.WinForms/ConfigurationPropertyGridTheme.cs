using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms;

internal static class ConfigurationPropertyGridTheme
{
    private const BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Apply(PropertyGrid propertyGrid)
    {
        propertyGrid.SelectedGridItemChanged -= PropertyGrid_SelectedGridItemChanged;
        propertyGrid.SelectedGridItemChanged += PropertyGrid_SelectedGridItemChanged;
        ApplyEditorButtons(propertyGrid);
    }

    private static void PropertyGrid_SelectedGridItemChanged(object? sender, SelectedGridItemChangedEventArgs e)
    {
        if (sender is not PropertyGrid propertyGrid || !propertyGrid.IsHandleCreated || propertyGrid.IsDisposed)
            return;

        try
        {
            propertyGrid.BeginInvoke((Action)(() =>
            {
                if (propertyGrid.IsDisposed ||
                    propertyGrid.Disposing ||
                    !propertyGrid.IsHandleCreated)
                {
                    return;
                }

                ApplyEditorButtons(propertyGrid);
            }));
        }
        catch (ObjectDisposedException)
        {
            // The form closed between the guard and BeginInvoke.
        }
        catch (InvalidOperationException)
        {
            // The grid handle was destroyed between the guard and BeginInvoke.
        }
    }

    private static void ApplyEditorButtons(PropertyGrid propertyGrid)
    {
        var gridView = FindPropertyGridView(propertyGrid);
        if (gridView is null)
            return;

        foreach (var button in GetEditorButtons(gridView))
            ConfigureEditorButton(button);
    }

    private static Control? FindPropertyGridView(Control owner)
    {
        foreach (Control child in owner.Controls)
        {
            if (child.GetType().Name == "PropertyGridView")
                return child;

            if (FindPropertyGridView(child) is { } nested)
                return nested;
        }

        return null;
    }

    private static IEnumerable<Button> GetEditorButtons(Control gridView)
    {
        var fields = gridView.GetType().GetFields(InstanceMembers)
            .Where(field => typeof(Button).IsAssignableFrom(field.FieldType))
            .Select(field => field.GetValue(gridView))
            .OfType<Button>();

        var properties = gridView.GetType().GetProperties(InstanceMembers)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => typeof(Button).IsAssignableFrom(property.PropertyType))
            .Select(property => TryGetButton(property, gridView))
            .OfType<Button>();

        return fields.Concat(properties).Distinct();
    }

    private static Button? TryGetButton(PropertyInfo property, object owner)
    {
        try
        {
            return property.GetValue(owner) as Button;
        }
        catch
        {
            return null;
        }
    }

    private static void ConfigureEditorButton(Button button)
    {
        button.BackColor = ConfigurationTheme.SurfaceRaised;
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = ConfigurationTheme.SurfaceSelected;
        button.FlatAppearance.MouseOverBackColor = ConfigurationTheme.SurfaceHover;
        button.FlatStyle = FlatStyle.Flat;
        button.ForeColor = ConfigurationTheme.TextPrimary;
        button.UseVisualStyleBackColor = false;
        RemoveNativeBorder(button);
    }

    private static void RemoveNativeBorder(Button button)
    {
        var styleProperty = button.GetType().GetProperty("ControlButtonStyle", InstanceMembers);
        if (styleProperty?.CanRead != true || styleProperty.CanWrite != true || !styleProperty.PropertyType.IsEnum)
            return;

        try
        {
            var styleValue = Convert.ToInt64(styleProperty.GetValue(button));
            foreach (var borderName in new[] { "RoundedBorder", "SingleBorder" })
            {
                if (Enum.TryParse(styleProperty.PropertyType, borderName, out var borderValue))
                    styleValue &= ~Convert.ToInt64(borderValue);
            }

            styleProperty.SetValue(button, Enum.ToObject(styleProperty.PropertyType, styleValue));
        }
        catch
        {
            // Keep the standard WinForms renderer if its internal enum changes.
        }
    }
}

using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Design;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace SysBot.Pokemon.WinForms;

internal static class ConfigurationCollectionEditor
{
    public static bool CanEdit(PropertyDescriptor property, object? value) =>
        !property.IsReadOnly && value is IList;

    public static void Edit(
        Control ownerControl,
        object owner,
        PropertyDescriptor property,
        object? value,
        Action valueChanged)
    {
        if (value is not IList)
            return;

        using var service = new CollectionEditorService(ownerControl.FindForm());
        var context = new CollectionEditorContext(owner, property, service);
        var editor =
            property.GetEditor(typeof(UITypeEditor)) as UITypeEditor ??
            TypeDescriptor.GetEditor(property.PropertyType, typeof(UITypeEditor)) as UITypeEditor ??
            new CollectionEditor(property.PropertyType);

        var edited = editor.EditValue(context, service, value);
        if (!service.Accepted)
            return;

        if (edited is not null && !ReferenceEquals(edited, value))
            property.SetValue(owner, edited);

        valueChanged();
    }

    private sealed class CollectionEditorContext(
        object instance,
        PropertyDescriptor property,
        IServiceProvider provider) : ITypeDescriptorContext
    {
        public IContainer? Container => null;
        public object Instance => instance;
        public PropertyDescriptor PropertyDescriptor => property;

        public object? GetService(Type serviceType) => provider.GetService(serviceType);
        public void OnComponentChanged() { }
        public bool OnComponentChanging() => true;
    }

    private sealed class CollectionEditorService(IWin32Window? owner)
        : IServiceProvider, IWindowsFormsEditorService, IDisposable
    {
        private ToolStripDropDown? _dropDown;
        public bool Accepted { get; private set; }

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IWindowsFormsEditorService) ? this : null;

        public void CloseDropDown() => _dropDown?.Close();

        public void DropDownControl(Control control)
        {
            _dropDown = new ToolStripDropDown
            {
                AutoClose = true,
                BackColor = ConfigurationTheme.SurfaceRaised,
                Padding = Padding.Empty,
                Renderer = new ConfigurationMenuRenderer(),
            };
            var host = new ToolStripControlHost(control)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = control.Size,
            };
            _dropDown.Items.Add(host);
            _dropDown.Show(Cursor.Position);
            while (_dropDown.Visible)
                Application.DoEvents();
            _dropDown.Dispose();
            _dropDown = null;
        }

        public DialogResult ShowDialog(Form dialog)
        {
            ThemeDialog(dialog);
            var result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            Accepted = result == DialogResult.OK;
            return result;
        }

        public void Dispose()
        {
            _dropDown?.Dispose();
            _dropDown = null;
        }

        private static void ThemeDialog(Form dialog)
        {
            dialog.BackColor = ConfigurationTheme.Canvas;
            dialog.ForeColor = ConfigurationTheme.TextPrimary;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.Shown += (_, _) =>
            {
                DarkModeHelper.SetDarkMode(dialog.Handle);
                DarkModeHelper.ApplyDarkModeToControlTree(dialog);
            };
            ThemeControlTree(dialog);
        }

        private static void ThemeControlTree(Control owner)
        {
            foreach (Control control in owner.Controls)
            {
                switch (control)
                {
                    case PropertyGrid propertyGrid:
                        propertyGrid.BackColor = ConfigurationTheme.Canvas;
                        propertyGrid.CategoryForeColor = ConfigurationTheme.TextPrimary;
                        propertyGrid.CommandsBackColor = ConfigurationTheme.Surface;
                        propertyGrid.CommandsForeColor = ConfigurationTheme.TextSecondary;
                        propertyGrid.HelpBackColor = ConfigurationTheme.Surface;
                        propertyGrid.HelpForeColor = ConfigurationTheme.TextSecondary;
                        propertyGrid.LineColor = ConfigurationTheme.Border;
                        propertyGrid.ViewBackColor = ConfigurationTheme.Surface;
                        propertyGrid.ViewForeColor = ConfigurationTheme.TextPrimary;
                        ConfigurationPropertyGridTheme.Apply(propertyGrid);
                        break;
                    case Button button:
                        button.BackColor = ConfigurationTheme.SurfaceRaised;
                        button.FlatAppearance.BorderColor = ConfigurationTheme.BorderStrong;
                        button.FlatStyle = FlatStyle.Flat;
                        button.ForeColor = ConfigurationTheme.TextPrimary;
                        break;
                    case TextBoxBase textBox:
                        textBox.BackColor = ConfigurationTheme.Surface;
                        textBox.ForeColor = ConfigurationTheme.TextPrimary;
                        break;
                    case ListBox listBox:
                        listBox.BackColor = ConfigurationTheme.Surface;
                        listBox.ForeColor = ConfigurationTheme.TextPrimary;
                        break;
                    case Label:
                        control.BackColor = Color.Transparent;
                        control.ForeColor = ConfigurationTheme.TextSecondary;
                        break;
                    case Panel or GroupBox:
                        control.BackColor = ConfigurationTheme.Canvas;
                        control.ForeColor = ConfigurationTheme.TextPrimary;
                        break;
                }

                ThemeControlTree(control);
            }
        }
    }
}

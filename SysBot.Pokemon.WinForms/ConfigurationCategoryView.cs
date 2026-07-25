using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SysBot.Pokemon.WinForms;

internal sealed record ConfigurationCategoryItem(string Name, string Description, object View)
{
    public override string ToString() => Name;
}

/// <summary>
/// Presents selected properties from an existing settings object without copying
/// values or replacing the original property descriptors and editors.
/// </summary>
internal sealed class PropertySubsetView(object owner, IEnumerable<string> propertyNames) : ICustomTypeDescriptor
{
    private readonly object _owner = owner;
    private readonly HashSet<string> _propertyNames = propertyNames.ToHashSet(StringComparer.Ordinal);

    public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(_owner);
    public string? GetClassName() => TypeDescriptor.GetClassName(_owner);
    public string? GetComponentName() => TypeDescriptor.GetComponentName(_owner);
    public TypeConverter GetConverter() => TypeDescriptor.GetConverter(_owner);
    public EventDescriptor? GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(_owner);
    public PropertyDescriptor? GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(_owner);
    public object? GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(_owner, editorBaseType);
    public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(_owner);
    public EventDescriptorCollection GetEvents(Attribute[]? attributes) =>
        attributes is null ? GetEvents() : TypeDescriptor.GetEvents(_owner, attributes);
    public PropertyDescriptorCollection GetProperties() => GetProperties(null);

    public PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
    {
        var properties = attributes is null
            ? TypeDescriptor.GetProperties(_owner)
            : TypeDescriptor.GetProperties(_owner, attributes);
        var selected = properties
            .Cast<PropertyDescriptor>()
            .Where(property => _propertyNames.Contains(property.Name))
            .ToArray();
        return new PropertyDescriptorCollection(selected, true);
    }

    public object GetPropertyOwner(PropertyDescriptor? propertyDescriptor) => _owner;
}

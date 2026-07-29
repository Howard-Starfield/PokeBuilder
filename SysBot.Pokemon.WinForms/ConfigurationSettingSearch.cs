using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SysBot.Pokemon.WinForms;

internal sealed record ConfigurationSearchEntry(
    string CategoryName,
    string Breadcrumb,
    string Title,
    string Description,
    object Owner,
    PropertyDescriptor Property);

internal sealed record ConfigurationSearchResult(ConfigurationSearchEntry Entry, int Score);

internal static class ConfigurationSettingSearch
{
    public static IReadOnlyList<ConfigurationSearchEntry> Build(
        IEnumerable<ConfigurationCategoryItem> categories)
    {
        var entries = new List<ConfigurationSearchEntry>();
        foreach (var category in categories.Where(category => !category.UsePropertyGrid))
            AddProperties(entries, category.Name, category.View, [], depth: 0);
        return entries;
    }

    public static IReadOnlyList<ConfigurationSearchResult> Find(
        IReadOnlyList<ConfigurationSearchEntry> entries,
        string query,
        int maximumResults = 80)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        return entries
            .Select(entry => new ConfigurationSearchResult(entry, GetScore(query, entry)))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(maximumResults)
            .ToArray();
    }

    private static int GetScore(string query, ConfigurationSearchEntry entry)
    {
        var titleScore = ConfigurationSearchMatcher.Score(query, entry.Title);
        var pathScore = ConfigurationSearchMatcher.Score(
            query,
            $"{entry.CategoryName} {entry.Breadcrumb} {entry.Title}");
        var descriptionScore = ConfigurationSearchMatcher.Score(query, entry.Description);
        return Math.Max(
            titleScore * 4,
            Math.Max(pathScore * 3, descriptionScore));
    }

    private static void AddProperties(
        ICollection<ConfigurationSearchEntry> entries,
        string categoryName,
        object component,
        IReadOnlyList<string> path,
        int depth)
    {
        var properties = TypeDescriptor.GetProperties(component)
            .Cast<PropertyDescriptor>()
            .Where(property => property.IsBrowsable);

        foreach (var property in properties)
        {
            var owner = GetPropertyOwner(component, property);
            var title = Humanize(property.DisplayName);
            var propertyGroup = Humanize(property.Category);
            var propertyPath = AppendDistinct(path, propertyGroup);
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
                    .Any(nested => nested.IsBrowsable) == true;
                if (nestedProperties)
                {
                    AddProperties(
                        entries,
                        categoryName,
                        value,
                        AppendDistinct(propertyPath, title),
                        depth + 1);
                    continue;
                }
            }

            entries.Add(new ConfigurationSearchEntry(
                categoryName,
                string.Join(" \u203a ", propertyPath),
                title,
                property.Description,
                owner,
                property));
        }
    }

    private static IReadOnlyList<string> AppendDistinct(
        IReadOnlyList<string> path,
        string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) ||
            (path.Count > 0 && string.Equals(path[^1], segment, StringComparison.CurrentCultureIgnoreCase)))
            return path;

        var result = path.ToList();
        result.Add(segment);
        return result;
    }

    private static object GetPropertyOwner(object component, PropertyDescriptor property)
    {
        if (component is ICustomTypeDescriptor descriptor)
            return descriptor.GetPropertyOwner(property) ?? component;
        return component;
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

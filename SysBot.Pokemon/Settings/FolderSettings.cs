using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace SysBot.Pokemon;

public class FolderSettings : IDumper
{
    private const string FeatureToggle = nameof(FeatureToggle);
    private const string Files = nameof(Files);
    public override string ToString() => "Folder / Dumping Settings";

    [Category(FeatureToggle), Description("When enabled, dumps any received PKM files (trade results) to the DumpFolder.")]
    public bool Dump { get; set; }

    [Category(Files), Description("Source folder: where PKM files to distribute are selected from.")]
    public string DistributeFolder { get; set; } = string.Empty;

    [Category(Files), Description("Destination folder: where all received PKM files are dumped to.")]
    public string DumpFolder { get; set; } = string.Empty;

    public IReadOnlyList<string> CreateDefaults(string path, bool enableDumpWhenNew = false)
    {
        var warnings = new List<string>();
        var defaultDump = Path.Combine(path, "dump");
        var defaultDistribute = Path.Combine(path, "distribute");

        var dumpWasMissing = string.IsNullOrWhiteSpace(DumpFolder);
        DumpFolder = EnsureFolder("DumpFolder", DumpFolder, defaultDump, warnings);
        if (dumpWasMissing && enableDumpWhenNew)
            Dump = true;

        DistributeFolder = EnsureFolder("DistributeFolder", DistributeFolder, defaultDistribute, warnings);
        return warnings;
    }

    private static string EnsureFolder(string settingName, string configuredPath, string fallbackPath, List<string> warnings)
    {
        var target = string.IsNullOrWhiteSpace(configuredPath) ? fallbackPath : configuredPath;
        if (TryCreateDirectory(target, out var error))
            return target;

        warnings.Add($"Config warning: {settingName} folder '{target}' could not be created or accessed ({error}); using '{fallbackPath}' instead.");
        if (TryCreateDirectory(fallbackPath, out _))
            return fallbackPath;

        return target;
    }

    private static bool TryCreateDirectory(string path, out string error)
    {
        try
        {
            Directory.CreateDirectory(path);
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }
}

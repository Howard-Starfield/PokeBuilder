using SysBot.Base;
using System;
using System.IO;

namespace SysBot.Pokemon.WinForms;

public static class ConfigLoader
{
    public static readonly string WorkingDirectory = Environment.CurrentDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
    public static string ConfigPath { get; internal set; } = Path.Combine(WorkingDirectory, "config.json");

    public static ProgramConfig LoadConfig(string? file) => LoadConfigWithResult(file).Config;

    public static ProgramConfigLoadResult LoadConfigWithResult(string? file)
    {
        if (file == null)
            file = ConfigPath;
        else
            ConfigPath = file;

        var result = ProgramConfigPersistence.LoadOrCreate(file);
        foreach (var warning in result.Config.Hub.Folder.CreateDefaults(WorkingDirectory, result.CreatedNew))
            LogUtil.LogError(warning, "Config");

        LogConfig.MaxArchiveFiles = result.Config.Hub.MaxArchiveFiles;
        LogConfig.LoggingEnabled = result.Config.Hub.LoggingEnabled;
        return result;
    }

    public static void Save(ProgramConfig cfg) => ProgramConfigPersistence.SaveAtomic(cfg, ConfigPath, out _);
}

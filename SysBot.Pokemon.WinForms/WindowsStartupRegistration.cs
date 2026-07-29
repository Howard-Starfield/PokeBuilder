using Microsoft.Win32;
using SysBot.Pokemon.Helpers;
using System;
using System.IO;

namespace SysBot.Pokemon.WinForms;

internal readonly record struct WindowsStartupRegistrationResult(
    bool Success,
    bool Changed,
    string? Error);

internal readonly record struct WindowsStartupRegistrationStatus(
    bool IsRegistered,
    bool MatchesExpectedCommand,
    string ExpectedCommand,
    string? RegisteredCommand,
    string? Error);

internal static class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static WindowsStartupRegistrationResult Apply(
        bool enabled,
        string executablePath,
        string configPath)
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (runKey is null)
                return new WindowsStartupRegistrationResult(false, false, "Windows did not provide access to the current-user startup registry key.");

            var existing = runKey.GetValue(PokeBotStartupCommand.WindowsRunValueName) as string;
            if (!enabled)
            {
                if (existing is null)
                    return new WindowsStartupRegistrationResult(true, false, null);

                runKey.DeleteValue(PokeBotStartupCommand.WindowsRunValueName, throwOnMissingValue: false);
                return new WindowsStartupRegistrationResult(true, true, null);
            }

            var desired = PokeBotStartupCommand.Build(executablePath, configPath);
            if (string.Equals(existing, desired, StringComparison.Ordinal))
                return new WindowsStartupRegistrationResult(true, false, null);

            runKey.SetValue(PokeBotStartupCommand.WindowsRunValueName, desired, RegistryValueKind.String);
            return new WindowsStartupRegistrationResult(true, true, null);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            System.Security.SecurityException or
            ArgumentException or
            IOException or
            InvalidOperationException)
        {
            return new WindowsStartupRegistrationResult(false, false, ex.Message);
        }
    }

    public static WindowsStartupRegistrationStatus Inspect(
        string executablePath,
        string configPath)
    {
        try
        {
            var expected = PokeBotStartupCommand.Build(executablePath, configPath);
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var registered = runKey?.GetValue(PokeBotStartupCommand.WindowsRunValueName) as string;
            return new WindowsStartupRegistrationStatus(
                registered is not null,
                string.Equals(registered, expected, StringComparison.Ordinal),
                expected,
                registered,
                null);
        }
        catch (Exception ex) when (
            ex is UnauthorizedAccessException or
            System.Security.SecurityException or
            ArgumentException or
            IOException or
            InvalidOperationException)
        {
            return new WindowsStartupRegistrationStatus(false, false, string.Empty, null, ex.Message);
        }
    }
}

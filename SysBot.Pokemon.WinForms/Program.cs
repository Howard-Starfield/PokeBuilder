using PKHeX.Core;
using SysBot.Pokemon.Helpers;
using SysBot.Pokemon.Z3;
using System;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms;

internal static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main()
    {
#if NETCOREAPP
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif
        var cfg = PokeBotLaunchArguments.FindConfigPath(Environment.GetCommandLineArgs());
        if (cfg != null)
            ConfigLoader.ConfigPath = cfg;

        PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Main());
    }
}

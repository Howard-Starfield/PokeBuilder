using PKHeX.Core;
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
        var cmd = Environment.GetCommandLineArgs();
        var cfg = Array.Find(cmd, z => z.EndsWith(".json"));
        if (cfg != null)
            ConfigLoader.ConfigPath = cfg;

        PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>();

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Main());
    }
}

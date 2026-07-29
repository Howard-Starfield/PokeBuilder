using System.ComponentModel;

namespace SysBot.Pokemon;

/// <summary>
/// Console agnostic settings
/// </summary>
public abstract class BaseConfig
{
    public const string DefaultRestartCronSchedule = "0 4 * * *";

    protected const string FeatureToggle = nameof(FeatureToggle);
    protected const string Operation = nameof(Operation);
    private const string Debug = nameof(Debug);
    private const string Startup = nameof(Startup);
    private const string ScheduledRestart = "Scheduled Restart";

    [Category(FeatureToggle), Description("When enabled, the bot will press the B button occasionally when it is not processing anything (to avoid sleep).")]
    public bool AntiIdle { get; set; }

    [Category(FeatureToggle), Description("Enables text logs. Restart to apply changes.")]
    public bool LoggingEnabled { get; set; } = true;

    [Category(FeatureToggle), Description("Maximum number of old text log files to retain. Set this to <= 0 to disable log cleanup. Restart to apply changes.")]
    public int MaxArchiveFiles { get; set; } = 14;

    [Category(Startup)]
    [DisplayName("Start PokeBot with Windows")]
    [Description("Launches PokeBot after the current Windows user signs in. No administrator access is required.")]
    public bool StartWithWindows { get; set; }

    [Category(Startup)]
    [DisplayName("Automatically start configured bots")]
    [Description("Starts every valid configured bot after PokeBot finishes loading.")]
    public bool AutoStartBots { get; set; }

    [Category(ScheduledRestart)]
    [DisplayName("Restart PokeBot on a schedule")]
    [Description("Restarts PokeBot automatically at the daily local time selected below.")]
    public bool ScheduledRestartEnabled { get; set; }

    [Category(ScheduledRestart)]
    [DisplayName("Current system time")]
    [Description("The local Windows time PokeBot uses to calculate the next restart.")]
    [System.Text.Json.Serialization.JsonIgnore]
    [Helpers.CurrentSystemTime]
    public System.DateTime CurrentSystemTime => System.DateTime.Now;

    [Category(ScheduledRestart)]
    [DisplayName("Daily restart time")]
    [Description("Choose the local system time when PokeBot should restart each day.")]
    [Helpers.RestartTimePicker]
    [TypeConverter(typeof(Helpers.CronExpressionConverter))]
    public string RestartCronSchedule { get; set; } = DefaultRestartCronSchedule;

    [Category(Debug), Description("Skips creating bots when the program is started; helpful for testing integrations.")]
    public bool SkipConsoleBotCreation { get; set; }

    [Category(Operation)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public LegalitySettings Legality { get; set; } = new();

    [Category(Operation)]
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public FolderSettings Folder { get; set; } = new();

    public abstract bool Shuffled { get; }
}

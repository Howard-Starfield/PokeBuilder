using SysBot.Base;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace SysBot.Pokemon;

public class ProgramConfig : BotList<PokeBotState>
{
    public const int CurrentConfigVersion = 1;
    public const int DefaultConfigurationFontScalePercent = 125;
    public const int MinConfigurationFontScalePercent = 100;
    public const int MaxConfigurationFontScalePercent = 200;

    public int ConfigVersion { get; set; } = CurrentConfigVersion;

    public PokeTradeHubConfig Hub { get; set; } = new();

    public ProgramMode Mode { get; set; } = ProgramMode.SV;

    public bool DarkMode { get; set; }

    [Category("Appearance")]
    [DisplayName("Configuration text size (%)")]
    [Description("Controls the text size in the Configuration screen. Use a value from 100 to 200 percent.")]
    [DefaultValue(DefaultConfigurationFontScalePercent)]
    public int ConfigurationFontScalePercent { get; set; } = DefaultConfigurationFontScalePercent;

    public int Width { get; set; }
    public int Height { get; set; }
}

public enum ProgramMode
{
    None = 0, // invalid

    SWSH = 1,

    BDSP = 2,

    LA = 3,

    SV = 4,

    LGPE = 5,

    LZA = 6,
}

[JsonSerializable(typeof(ProgramConfig))]
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class ProgramConfigContext : JsonSerializerContext;

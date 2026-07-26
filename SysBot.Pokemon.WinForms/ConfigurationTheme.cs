using System;
using System.Drawing;

namespace SysBot.Pokemon.WinForms;

internal static class ConfigurationTheme
{
    public static Color Canvas { get; } = Color.FromArgb(29, 38, 49);
    public static Color Surface { get; } = Color.FromArgb(23, 29, 37);
    public static Color SurfaceRaised { get; } = Color.FromArgb(27, 35, 45);
    public static Color SurfaceHover { get; } = Color.FromArgb(37, 46, 58);
    public static Color SurfaceSelected { get; } = Color.FromArgb(48, 34, 39);
    public static Color Border { get; } = Color.FromArgb(48, 60, 73);
    public static Color BorderStrong { get; } = Color.FromArgb(72, 84, 98);
    public static Color TextPrimary { get; } = Color.FromArgb(244, 246, 248);
    public static Color TextSecondary { get; } = Color.FromArgb(205, 214, 223);
    public static Color TextMuted { get; } = Color.FromArgb(158, 172, 189);
    public static Color Accent { get; } = Color.FromArgb(230, 77, 77);
    public static Color AccentPressed { get; } = Color.FromArgb(177, 55, 55);
    public static Color Ink { get; } = Color.FromArgb(17, 22, 29);

    public static int ScalePixels(int pixels, int percent, int minimum = 1)
    {
        var clampedPercent = Math.Clamp(percent, 100, 200);
        return Math.Max(minimum, (int)Math.Round(pixels * clampedPercent / 100F));
    }

    public static float ScaleFont(float points, int percent)
    {
        var clampedPercent = Math.Clamp(percent, 100, 200);
        return points * clampedPercent / 100F;
    }
}

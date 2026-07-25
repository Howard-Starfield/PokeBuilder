using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SysBot.Pokemon.Discord;

internal static class TradeEmbedImageSizer
{
    internal static int GetPixelSize(TradeSettings.ImageSize imageSize) =>
        imageSize switch
        {
            TradeSettings.ImageSize.Size128x128 => 128,
            TradeSettings.ImageSize.Size256x256 => 256,
            _ => 256,
        };

    internal static Bitmap Resize(Image source, TradeSettings.ImageSize imageSize)
    {
        int targetSize = GetPixelSize(imageSize);
#pragma warning disable CA1416 // PokeBot Discord is run on Windows.
        var resized = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(resized);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        double scale = Math.Min((double)targetSize / source.Width, (double)targetSize / source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        int x = (targetSize - width) / 2;
        int y = (targetSize - height) / 2;
        graphics.DrawImage(source, new Rectangle(x, y, width, height));
#pragma warning restore CA1416

        return resized;
    }
}

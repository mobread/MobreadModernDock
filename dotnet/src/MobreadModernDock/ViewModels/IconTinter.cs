using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MobreadModernDock.ViewModels;

/// <summary>
/// Applies an iOS-18-style duotone tint to dock icons: every pixel keeps its
/// alpha and luminance, but its hue is replaced by the tint color
/// (result = tint color * luminance). The icons stay recognizable and keep
/// their shading, just like "Tinted" icons on iOS 18.
/// </summary>
public static class IconTinter
{
    /// <summary>Active tint color; null means icons render untinted.</summary>
    public static Color? ActiveColor { get; set; }

    /// <summary>
    /// Returns the tinted copy of <paramref name="source"/>, or the source
    /// bitmap unchanged when no tint is active. Never throws: on any failure
    /// the original bitmap is returned.
    /// </summary>
    public static Bitmap? Apply(Bitmap? source)
        => source == null || ActiveColor == null ? source : Tint(source, ActiveColor.Value);

    private static Bitmap Tint(Bitmap source, Color tint)
    {
        try
        {
            // Round-trip through System.Drawing (PNG) for a straight-ARGB buffer.
            using var inStream = new MemoryStream();
            source.Save(inStream);
            inStream.Position = 0;
            using var sysSource = new System.Drawing.Bitmap(inStream);

            int w = sysSource.Width;
            int h = sysSource.Height;
            var result = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            double tr = tint.R, tg = tint.G, tb = tint.B;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var p = sysSource.GetPixel(x, y);
                    if (p.A == 0) continue;
                    int lum = (p.R + p.G + p.B) / 3;
                    result.SetPixel(x, y, System.Drawing.Color.FromArgb(
                        p.A,
                        (int)Math.Min(255, tr * lum / 255.0),
                        (int)Math.Min(255, tg * lum / 255.0),
                        (int)Math.Min(255, tb * lum / 255.0)));
                }
            }

            using var outStream = new MemoryStream();
            result.Save(outStream, System.Drawing.Imaging.ImageFormat.Png);
            outStream.Position = 0;
            return new Bitmap(outStream);
        }
        catch
        {
            return source;
        }
    }
}

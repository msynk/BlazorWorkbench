namespace BlazorWorkbench.Testing.Imaging;

/// <summary>Result of comparing two screenshots.</summary>
public sealed record ImageComparison(bool SizeMismatch, long DiffPixels, long TotalPixels, PngImage Diff, long AntiAliasedPixels = 0)
{
    public double DiffRatio => TotalPixels == 0 ? 0 : (double)DiffPixels / TotalPixels;
}

/// <summary>
/// Perceptual pixel comparison. Colors are compared in YIQ space (as in pixelmatch), weighted toward luminance, after
/// blending with white. <c>threshold</c> (0..1) is the tolerated color distance per pixel; 0.1 ignores imperceptible
/// rendering noise while detecting real changes. Pixels that differ only because of anti-aliasing (detected with the
/// intensity-slope method of Vyšniauskas, 2009) are ignored by default and drawn in yellow in the diff image.
/// </summary>
public static class ImageComparer
{
    private const double MaxYiqDelta = 35215;

    public static ImageComparison Compare(PngImage baseline, PngImage actual, double threshold = 0.1, bool ignoreAntiAliasing = true)
    {
        var width = Math.Max(baseline.Width, actual.Width);
        var height = Math.Max(baseline.Height, actual.Height);
        var sameSize = baseline.Width == actual.Width && baseline.Height == actual.Height;
        var diff = new PngImage(width, height);
        var maxDelta = MaxYiqDelta * threshold * threshold;
        long different = 0, antiAliased = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                var inBase = x < baseline.Width && y < baseline.Height;
                var inActual = x < actual.Width && y < actual.Height;
                if (!inBase || !inActual)
                {
                    different++;
                    SetPixel(diff, o, 255, 0, 255);
                    continue;
                }

                var bo = (y * baseline.Width + x) * 4;
                var ao = (y * actual.Width + x) * 4;
                var delta = ColorDelta(baseline.Pixels, bo, actual.Pixels, ao);
                if (delta > maxDelta)
                {
                    if (ignoreAntiAliasing && sameSize &&
                        (IsAntiAliased(baseline, x, y, actual) || IsAntiAliased(actual, x, y, baseline)))
                    {
                        antiAliased++;
                        SetPixel(diff, o, 255, 200, 0);
                    }
                    else
                    {
                        different++;
                        SetPixel(diff, o, 255, 0, 0);
                    }
                }
                else
                {
                    // Faded grayscale of the baseline gives context around the changes.
                    var gray = (byte)(255 - (255 - Luma(baseline.Pixels, bo)) * 0.15);
                    SetPixel(diff, o, gray, gray, gray);
                }
            }
        }

        return new ImageComparison(!sameSize, different, (long)width * height, diff, antiAliased);
    }

    /// <summary>
    /// True when the pixel looks like an anti-aliased edge: its neighbors include both a darker and a brighter pixel,
    /// and one of those extremes sits in a flat region (several identical siblings) in both images.
    /// </summary>
    internal static bool IsAntiAliased(PngImage image, int x1, int y1, PngImage other)
    {
        int width = image.Width, height = image.Height;
        int x0 = Math.Max(x1 - 1, 0), y0 = Math.Max(y1 - 1, 0), x2 = Math.Min(x1 + 1, width - 1), y2 = Math.Min(y1 + 1, height - 1);
        var pos = (y1 * width + x1) * 4;
        var zeroes = x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2 ? 1 : 0;
        double min = 0, max = 0;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;

        for (var x = x0; x <= x2; x++)
        {
            for (var y = y0; y <= y2; y++)
            {
                if (x == x1 && y == y1)
                    continue;

                var delta = BrightnessDelta(image.Pixels, pos, image.Pixels, (y * width + x) * 4);
                if (delta == 0)
                {
                    if (++zeroes > 2)
                        return false;
                }
                else if (delta < min)
                {
                    min = delta;
                    minX = x;
                    minY = y;
                }
                else if (delta > max)
                {
                    max = delta;
                    maxX = x;
                    maxY = y;
                }
            }
        }

        if (min == 0 || max == 0)
            return false;

        return (HasManySiblings(image, minX, minY) && HasManySiblings(other, minX, minY)) ||
               (HasManySiblings(image, maxX, maxY) && HasManySiblings(other, maxX, maxY));
    }

    private static bool HasManySiblings(PngImage image, int x1, int y1)
    {
        int width = image.Width, height = image.Height;
        int x0 = Math.Max(x1 - 1, 0), y0 = Math.Max(y1 - 1, 0), x2 = Math.Min(x1 + 1, width - 1), y2 = Math.Min(y1 + 1, height - 1);
        var pos = (y1 * width + x1) * 4;
        var zeroes = x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2 ? 1 : 0;
        var p = image.Pixels;

        for (var x = x0; x <= x2; x++)
        {
            for (var y = y0; y <= y2; y++)
            {
                if (x == x1 && y == y1)
                    continue;
                var o = (y * width + x) * 4;
                if (p[pos] == p[o] && p[pos + 1] == p[o + 1] && p[pos + 2] == p[o + 2] && p[pos + 3] == p[o + 3])
                    zeroes++;
                if (zeroes > 2)
                    return true;
            }
        }

        return false;
    }

    private static void SetPixel(PngImage image, int o, byte r, byte g, byte b)
    {
        image.Pixels[o] = r;
        image.Pixels[o + 1] = g;
        image.Pixels[o + 2] = b;
        image.Pixels[o + 3] = 255;
    }

    private static double Luma(byte[] p, int o) => Blend(p[o], p[o + 3]) * 0.299 + Blend(p[o + 1], p[o + 3]) * 0.587 + Blend(p[o + 2], p[o + 3]) * 0.114;

    private static double Blend(byte c, byte a) => 255 + (c - 255) * (a / 255.0);

    private static double BrightnessDelta(byte[] a, int ao, byte[] b, int bo) =>
        Y(Blend(a[ao], a[ao + 3]), Blend(a[ao + 1], a[ao + 3]), Blend(a[ao + 2], a[ao + 3])) -
        Y(Blend(b[bo], b[bo + 3]), Blend(b[bo + 1], b[bo + 3]), Blend(b[bo + 2], b[bo + 3]));

    private static double ColorDelta(byte[] a, int ao, byte[] b, int bo)
    {
        if (a[ao] == b[bo] && a[ao + 1] == b[bo + 1] && a[ao + 2] == b[bo + 2] && a[ao + 3] == b[bo + 3])
            return 0;

        double r1 = Blend(a[ao], a[ao + 3]), g1 = Blend(a[ao + 1], a[ao + 3]), b1 = Blend(a[ao + 2], a[ao + 3]);
        double r2 = Blend(b[bo], b[bo + 3]), g2 = Blend(b[bo + 1], b[bo + 3]), b2 = Blend(b[bo + 2], b[bo + 3]);

        var y = Y(r1, g1, b1) - Y(r2, g2, b2);
        var i = I(r1, g1, b1) - I(r2, g2, b2);
        var q = Q(r1, g1, b1) - Q(r2, g2, b2);
        return 0.5053 * y * y + 0.299 * i * i + 0.1957 * q * q;
    }

    private static double Y(double r, double g, double b) => r * 0.29889531 + g * 0.58662247 + b * 0.11448223;
    private static double I(double r, double g, double b) => r * 0.59597799 - g * 0.27417610 - b * 0.32180189;
    private static double Q(double r, double g, double b) => r * 0.21147017 - g * 0.52261711 + b * 0.31114694;
}

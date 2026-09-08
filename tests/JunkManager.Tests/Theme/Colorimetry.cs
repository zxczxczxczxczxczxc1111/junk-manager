namespace JunkManager.Tests.Theme;

/// <summary>
/// WCAG contrast and CIELAB, hand written on purpose: the numbers in the spec
/// were produced by an external validator, and a test that calls the same
/// validator proves nothing about the values that actually ship.
/// </summary>
public static class Colorimetry
{
    public static (byte R, byte G, byte B) Rgb(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);

        var s = hex.Trim().TrimStart('#');
        if (s.Length == 8)
        {
            s = s[2..];   // drop the alpha byte, WPF writes #AARRGGBB
        }

        return (
            Convert.ToByte(s[..2], 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16));
    }

    private static double Linear(byte value)
    {
        var c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    public static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        return (0.2126 * Linear(r)) + (0.7152 * Linear(g)) + (0.0722 * Linear(b));
    }

    public static double Contrast(string a, string b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static (double L, double A, double B) Lab(string hex)
    {
        var (r8, g8, b8) = Rgb(hex);
        double r = Linear(r8), g = Linear(g8), b = Linear(b8);

        var x = ((0.4124 * r) + (0.3576 * g) + (0.1805 * b)) / 0.95047;
        var y = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        var z = ((0.0193 * r) + (0.1192 * g) + (0.9505 * b)) / 1.08883;

        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : ((903.3 * t) + 16) / 116;

        double fx = F(x), fy = F(y), fz = F(z);
        return ((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    public static double Lightness(string hex) => Lab(hex).L;

    /// <summary>Hue angle in degrees, 0 to 360.</summary>
    public static double Hue(string hex)
    {
        var (_, a, b) = Lab(hex);
        var deg = Math.Atan2(b, a) * 180 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }

    /// <summary>Shortest distance between two hue angles, 0 to 180.</summary>
    public static double HueDistance(string a, string b)
    {
        var d = Math.Abs(Hue(a) - Hue(b)) % 360;
        return d > 180 ? 360 - d : d;
    }
}

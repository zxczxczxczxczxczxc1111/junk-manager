using System.Globalization;

namespace JunkManager.App.Text;

/// <summary>
/// Bytes into the string a person reads. Binary units, Russian abbreviations,
/// comma as the decimal mark whatever the thread culture says.
/// </summary>
/// <remarks>
/// Every number this produces was measured by walking the tree. Nothing here
/// estimates: the product refuses to show a size it did not count, and a
/// formatter that quietly rounds a guess into "18,7 ГБ" would undo that.
/// </remarks>
internal static class ByteSizeFormatter
{
    private static readonly CultureInfo Russkaya = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] Edinicy = ["Б", "КБ", "МБ", "ГБ", "ТБ", "ПБ"];

    public static string Format(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        if (bytes < 1024)
        {
            return string.Create(Russkaya, $"{bytes} {Edinicy[0]}");
        }

        double znachenie = bytes;
        var stupen = 0;

        while (znachenie >= 1024 && stupen < Edinicy.Length - 1)
        {
            znachenie /= 1024;
            stupen++;
        }

        // One decimal below 100, none above it. Above 100 the tenth is noise:
        // it changes while the person reads the row and buys no information.
        return znachenie < 100
            ? string.Create(Russkaya, $"{znachenie:0.0} {Edinicy[stupen]}")
            : string.Create(Russkaya, $"{znachenie:0} {Edinicy[stupen]}");
    }
}

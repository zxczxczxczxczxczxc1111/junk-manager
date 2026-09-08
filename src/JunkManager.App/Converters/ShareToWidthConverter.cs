using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JunkManager.App.Converters;

/// <summary>
/// A share from 0 to 1 and a track width into a pixel width.
/// </summary>
/// <remarks>
/// Две величины, а не одна с параметром: ширина дорожки известна только во
/// время раскладки, и передать её через ConverterParameter нечем. Отсюда
/// многозначная привязка, а не обычная.
/// </remarks>
internal sealed class ShareToWidthConverter : IMultiValueConverter
{
    public object Convert(
        object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);

        // UnsetValue приходит на первом проходе раскладки, NaN приходит от
        // ActualWidth до первого измерения. Оба обязаны дать ноль: ширина NaN
        // это исключение в измерении, а не тонкая полоска.
        if (values.Length != 2
            || values[0] is not double dolya
            || values[1] is not double shirina
            || double.IsNaN(dolya) || double.IsNaN(shirina)
            || double.IsInfinity(shirina))
        {
            return 0d;
        }

        return Math.Clamp(dolya, 0, 1) * Math.Max(shirina, 0);
    }

    public object[] ConvertBack(
        object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("ширина только читается");
}

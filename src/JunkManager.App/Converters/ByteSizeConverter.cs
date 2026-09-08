using System.Globalization;
using System.Windows.Data;
using JunkManager.App.Text;

namespace JunkManager.App.Converters;

/// <summary>
/// Binding entry point for <see cref="ByteSizeFormatter"/>. It exists so no
/// view model has to hold a pre-formatted string next to the number: two fields
/// that must agree eventually stop agreeing.
/// </summary>
[ValueConversion(typeof(long), typeof(string))]
internal sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            // int тоже: размер обычно long, но счётчик байт с привязки к int-у
            // прилетает целым, и молча пустая подпись в этом месте читается
            // как «размер не посчитали».
            long bayt => ByteSizeFormatter.Format(bayt),
            int bayt => ByteSizeFormatter.Format(bayt),
            _ => string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("размер только читается");
}

using System.Globalization;
using System.Windows.Data;

namespace JunkManager.App.Converters;

/// <summary>
/// Null to true. It exists for exactly one binding: an unknown total turns the
/// progress bar indeterminate. Folding this into BoolToVisibilityConverter
/// would give that class two jobs and a parameter to choose between them.
/// </summary>
[ValueConversion(typeof(object), typeof(bool))]
internal sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("признак только читается");
}

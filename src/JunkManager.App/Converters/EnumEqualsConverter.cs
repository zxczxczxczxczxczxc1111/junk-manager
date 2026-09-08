using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JunkManager.App.Converters;

/// <summary>
/// True when the bound value equals the enum member named in the parameter.
/// Compares by name and not by boxed value: an enum whose members change order
/// would otherwise start matching the wrong branch, and nothing would say so.
/// </summary>
/// <remarks>
/// Отвечает в том типе, который спросили. WPF НЕ приводит bool к Visibility сам:
/// привязка видимости к преобразователю, отдающему bool, молча оставляет
/// элемент видимым, и все четыре шага потока оказываются на экране разом.
/// Второй класс ради этого не заводится: вопрос один и тот же, «равно ли», и
/// две реализации одного вопроса рано или поздно отвечают по-разному.
/// </remarks>
[ValueConversion(typeof(Enum), typeof(bool))]
internal sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var ravno = value is Enum znachenie
            && parameter is string imya
            && string.Equals(znachenie.ToString(), imya, StringComparison.Ordinal);

        if (targetType == typeof(Visibility))
        {
            return ravno ? Visibility.Visible : Visibility.Collapsed;
        }

        return ravno;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("сравнение только читается");
}

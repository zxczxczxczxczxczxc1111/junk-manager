using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JunkManager.App.Converters;

/// <summary>
/// True to Visible, false to Collapsed. Collapsed and not Hidden: a hidden
/// element keeps its size, and a hidden restriction note would leave a band of
/// empty screen above every list on a machine that never refused elevation.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>Set to true to swap the answer without writing a second class.</summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Строка тоже считается признаком: подпись кнопки в пустом состоянии
        // привязана сюда напрямую, а не через второй преобразователь.
        var da = value switch
        {
            bool priznak => priznak,
            string tekst => !string.IsNullOrEmpty(tekst),

            // Ноль это «нечего показывать», а не «что-то есть». Без этой ветки
            // счётчик ноль попадал в _ => true, и строка «0 строк без удаления»
            // висела под каждым чистым прогоном.
            int schet => schet != 0,
            long schet => schet != 0,

            null => false,
            _ => true,
        };

        return da != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("видимость только читается");
}

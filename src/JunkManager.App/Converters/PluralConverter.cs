using System.Globalization;
using System.Windows.Data;
using JunkManager.App.Text;

namespace JunkManager.App.Converters;

/// <summary>
/// Number plus an agreed noun. Forms come through ConverterParameter as
/// "след|следа|следов", because a screen needs a dozen different nouns and a
/// converter per noun is a dozen classes that do one thing.
/// </summary>
[ValueConversion(typeof(long), typeof(string))]
internal sealed class PluralConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // int И long. Счётчик в модели это int, размер это long, и привязка
        // отдаёт ровно тот тип, что объявлен у свойства. Проверка только на
        // long давала пустую строку на каждом счётчике, причём молча: пустая
        // подпись под именем категории выглядит как «подписи тут и не было».
        long skolko;
        switch (value)
        {
            case long dlinnoe: skolko = dlinnoe; break;
            case int koroktoe: skolko = koroktoe; break;
            default: return string.Empty;
        }

        if (parameter is not string formy)
        {
            return string.Empty;
        }

        var chasti = formy.Split('|');

        // Three forms or nothing. Two forms would silently produce "5 следа" for
        // every count that needs the third, which is the exact defect this class
        // was written to make impossible.
        return chasti.Length == 3
            ? RussianPlural.Format(skolko, chasti[0], chasti[1], chasti[2])
            : throw new ArgumentException(
                $"нужны три формы через вертикальную черту, пришло: {formy}", nameof(parameter));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("подпись только читается");
}

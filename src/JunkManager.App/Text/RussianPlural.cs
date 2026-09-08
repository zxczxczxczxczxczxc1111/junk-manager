using System.Globalization;

namespace JunkManager.App.Text;

/// <summary>
/// The only place in the product where Russian noun agreement lives. One place
/// on purpose: the rule has three branches and two exceptions, and a second
/// copy of it is a second chance to get "5 следа" onto a screen.
/// </summary>
internal static class RussianPlural
{
    // Russian formatting is pinned, not taken from the thread. The polygon guest
    // runs an English locale, and a number formatted there with the thread
    // culture reads "3,412", which nobody notices until a screenshot comes back.
    private static readonly CultureInfo Russkaya = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>
    /// Picks the noun form for <paramref name="count"/>: one, few (2-4) or many.
    /// </summary>
    /// <param name="count">Сколько штук. Знак не важен, берётся модуль.</param>
    /// <param name="one">Форма при 1, 21, 31: «след».</param>
    /// <param name="few">Форма при 2-4, 22-24: «следа».</param>
    /// <param name="many">Форма при 0, 5-20, 25-30: «следов».</param>
    public static string Choose(long count, string one, string few, string many)
    {
        // Math.Abs(long.MinValue) кидает OverflowException. Число находок туда
        // не доедет никогда, но метод не должен уметь падать вовсе.
        var absolyutnoe = count == long.MinValue ? long.MaxValue : Math.Abs(count);
        var edinicy = absolyutnoe % 10;
        var desyatki = absolyutnoe % 100;

        // 11 to 14 take the "many" form whatever the last digit says. This is the
        // exception that a naive switch on the last digit gets wrong, and it is
        // exactly the range a list of findings lands in most often.
        if (desyatki is >= 11 and <= 14)
        {
            return many;
        }

        return edinicy switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    /// <summary>Число с разрядами плюс согласованное слово: «3 412 объектов».</summary>
    public static string Format(long count, string one, string few, string many) =>
        string.Create(Russkaya, $"{count:N0} {Choose(count, one, few, many)}");
}

using System.Drawing;
using FluentAssertions;
using JunkManager.Tests.Theme;

namespace JunkManager.Tests.Ui;

/// <summary>
/// Compares pixels on the running window with the colour tokens in Tokens.xaml.
/// </summary>
/// <remarks>
/// The point is the gap between "the token says lilac" and "the pixel is lilac".
/// A merged dictionary loaded in the wrong order, a system brush leaking through
/// a template, a theme file that stopped being copied: every one of those keeps
/// the markup tests green and shows up here.
/// </remarks>
public static class UiPalette
{
    /// <summary>
    /// Допуск на канал. Не ноль: Mica подмешивает фон рабочего стола под
    /// поверхностями окна, и требовать точного совпадения значит требовать
    /// выключить Mica.
    /// </summary>
    public const int DopuskPoUmolchaniyu = 10;

    public static Color Pixel(Bitmap snimok, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(snimok);

        x.Should().BeInRange(0, snimok.Width - 1);
        y.Should().BeInRange(0, snimok.Height - 1);

        return snimok.GetPixel(x, y);
    }

    /// <summary>Цвет токена из Tokens.xaml по его ключу, например AccentColor.</summary>
    public static Color Token(string klyuch)
    {
        var cveta = XamlSource.KeyedValues(XamlSource.Load("Tokens.xaml"), "Color");
        cveta.Should().ContainKey(klyuch);

        var (r, g, b) = Colorimetry.Rgb(cveta[klyuch]);
        return Color.FromArgb(r, g, b);
    }

    public static bool Blizko(Color chto, Color kChemu, int dopusk = DopuskPoUmolchaniyu) =>
        Math.Abs(chto.R - kChemu.R) <= dopusk
        && Math.Abs(chto.G - kChemu.G) <= dopusk
        && Math.Abs(chto.B - kChemu.B) <= dopusk;

    /// <summary>
    /// Есть ли рядом с точкой пиксель нужного цвета. Полоса метки шириной в две
    /// точки, и попасть в неё одним пикселем на разных масштабах нельзя: на 150
    /// процентах она смещается на полторы точки.
    /// </summary>
    public static bool EstRyadom(
        Bitmap snimok, int x, int y, Color cvet, int radius = 4, int dopusk = DopuskPoUmolchaniyu)
    {
        ArgumentNullException.ThrowIfNull(snimok);

        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var tx = x + dx;
                var ty = y + dy;

                if (tx < 0 || ty < 0 || tx >= snimok.Width || ty >= snimok.Height)
                {
                    continue;
                }

                if (Blizko(snimok.GetPixel(tx, ty), cvet, dopusk))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Кладёт снимок туда, откуда его заберёт хост.
    /// </summary>
    /// <remarks>
    /// Разбирать пиксельное падение по одному числу в сообщении нельзя: «362
    /// синих пикселя» не говорит, ЧТО синее. Снимок едет в C:\poligon рядом с
    /// отчётом, скрипт прогона забирает оттуда все png.
    /// </remarks>
    public static string Sohranit(Bitmap snimok, string imya)
    {
        ArgumentNullException.ThrowIfNull(snimok);

        var katalog = Directory.Exists(@"C:\poligon") ? @"C:\poligon" : AppContext.BaseDirectory;
        var put = Path.Combine(katalog, imya + ".png");
        snimok.Save(put, System.Drawing.Imaging.ImageFormat.Png);
        return put;
    }

    public static bool Siniy(Color c) =>
        c.B > 150 && c.B - c.R > 60 && c.B - c.G > 30;

    /// <summary>
    /// Синий, занимающий сплошной квадрат <see cref="Storona"/> на
    /// <see cref="Storona"/>, а не отдельные точки.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Без этого проверка считает не выделение, а СУБПИКСЕЛЬНОЕ СГЛАЖИВАНИЕ
    /// ТЕКСТА. ClearType рисует левый край вертикального штриха синим, правый
    /// оранжевым: на снимке живого окна 05.09.2026 набралось 362 таких точки
    /// цветами вроде #43A9DA, рамка накрыла всё окно, и выглядело это как
    /// системное выделение, протёкшее сквозь шаблон.
    /// </para>
    /// <para>
    /// Соседей на расстоянии четырёх точек мало: буквы стоят плотно, и слева
    /// от каждого штриха свой синий край. Нужен именно СПЛОШНОЙ квадрат,
    /// какого у сглаживания не бывает. Проверено на том же снимке: сплошных
    /// квадратов ноль, а вклеенная в него полоса выделения #0078D4 даёт 594.
    /// </para>
    /// </remarks>
    public static bool SploshnoSiniy(Bitmap snimok, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(snimok);

        if (x + Storona > snimok.Width || y + Storona > snimok.Height)
        {
            return false;
        }

        for (var dx = 0; dx < Storona; dx++)
        {
            for (var dy = 0; dy < Storona; dy++)
            {
                if (!Siniy(UiPalette.Pixel(snimok, x + dx, y + dy)))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public const int Storona = 5;
}

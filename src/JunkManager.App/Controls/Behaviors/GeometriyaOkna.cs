using System.Windows;

namespace JunkManager.App.Controls.Behaviors;

/// <summary>Размеры и положение окна, как их видит WPF: в точках.</summary>
internal readonly record struct Geometriya(
    double Shirina, double Vysota, double Sleva, double Sverhu,
    double MinShirina, double MinVysota);

/// <summary>
/// Fits the window into the work area. Separated from the window so the
/// arithmetic can be checked without a screen.
/// </summary>
/// <remarks>
/// Проверять это только на живом окне дорого: у гостя один экран и один
/// масштаб за прогон, то есть каждая правка стоит перезагрузки и семи минут.
/// Арифметика здесь, живая проверка на масштабах в классе Ui.
/// </remarks>
internal static class GeometriyaOkna
{
    public static Geometriya PodEkran(Geometriya okno, Rect rabochaya)
    {
        // Минимумы ужимаются ПЕРВЫМИ и вместе с размерами: минимум больше
        // экрана перебивает новую высоту, и окно останется за краем. На 200
        // процентах минимум 720 точек это 1440 пикселей, то есть больше всего
        // экрана 1080.
        var minShirina = Math.Min(okno.MinShirina, rabochaya.Width);
        var minVysota = Math.Min(okno.MinVysota, rabochaya.Height);

        var shirina = Math.Clamp(okno.Shirina, minShirina, rabochaya.Width);
        var vysota = Math.Clamp(okno.Vysota, minVysota, rabochaya.Height);

        // Положение правится после размера: WPF поставил окно по прежним
        // размерам, и ужатое оказывается смещённым на разницу.
        var sleva = Math.Max(rabochaya.Left, Math.Min(okno.Sleva, rabochaya.Right - shirina));
        var sverhu = Math.Max(rabochaya.Top, Math.Min(okno.Sverhu, rabochaya.Bottom - vysota));

        return new Geometriya(shirina, vysota, sleva, sverhu, minShirina, minVysota);
    }
}

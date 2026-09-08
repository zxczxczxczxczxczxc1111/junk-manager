using System.Windows;

namespace JunkManager.App.Controls.Behaviors;

/// <summary>
/// Shrinks the window into the work area before it is shown.
/// </summary>
/// <remarks>
/// <para>
/// Размеры в разметке заданы в точках, а экран меряется в пикселях: 880 точек
/// это 1100 пикселей на масштабе 125 процентов и 1760 на 200. На экране
/// 1920x1080 окно перестаёт помещаться, нижняя часть вместе с кнопками уходит
/// за край, и WPF сам ничего не ужимает. Проверено прогонами в госте
/// 05.09.2026 на четырёх масштабах: на 125 окно было 1100 пикселей высотой при
/// рабочей области 1022.
/// </para>
/// <para>
/// Поведением, а не кодом за разметкой: правило проекта держит code-behind
/// пустым, и его сторожит проверка метаданных. Арифметика лежит отдельно в
/// <see cref="GeometriyaOkna"/> и проверяется без экрана.
/// </para>
/// </remarks>
internal static class PodgonPodEkran
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(PodgonPodEkran),
            new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EnabledProperty, value);
    }

    public static bool GetEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EnabledProperty);
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window okno || e.NewValue is not true)
        {
            return;
        }

        // SourceInitialized, а не Loaded: к моменту Loaded окно уже показано, и
        // ужатие видно человеку рывком.
        okno.SourceInitialized += (_, _) => Prilozhit(okno, SystemParameters.WorkArea);
    }

    internal static void Prilozhit(Window okno, Rect rabochaya)
    {
        ArgumentNullException.ThrowIfNull(okno);

        var itog = GeometriyaOkna.PodEkran(
            new Geometriya(okno.Width, okno.Height, okno.Left, okno.Top, okno.MinWidth, okno.MinHeight),
            rabochaya);

        okno.MinWidth = itog.MinShirina;
        okno.MinHeight = itog.MinVysota;
        okno.Width = itog.Shirina;
        okno.Height = itog.Vysota;
        okno.Left = itog.Sleva;
        okno.Top = itog.Sverhu;
    }
}

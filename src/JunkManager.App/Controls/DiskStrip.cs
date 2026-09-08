using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using JunkManager.App.Text;

namespace JunkManager.App.Controls;

/// <summary>
/// The volume bar: occupied by other things, found as junk, free. Ten pixels
/// high, three segments, no labels inside it.
/// </summary>
/// <remarks>
/// <para>
/// Drawn rather than assembled from three Border elements with star widths: a
/// star layout rounds each share independently, and at 1920 the three rounded
/// widths do not add up to the track, which shows as a one pixel gap that moves
/// as the window resizes.
/// </para>
/// <para>
/// A segment thinner than <see cref="MinimalnayaShirina"/> is drawn at that
/// width anyway and taken out of the largest neighbour. A share of 0.3 percent
/// is a real thing that a person is entitled to see; a segment rounded to zero
/// pixels is a lie told by arithmetic.
/// </para>
/// </remarks>
internal sealed class DiskStrip : FrameworkElement
{
    private const double MinimalnayaShirina = 3;

    public static readonly DependencyProperty TotalBytesProperty =
        DependencyProperty.Register(
            nameof(TotalBytes), typeof(long), typeof(DiskStrip),
            new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UsedBytesProperty =
        DependencyProperty.Register(
            nameof(UsedBytes), typeof(long), typeof(DiskStrip),
            new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FoundBytesProperty =
        DependencyProperty.Register(
            nameof(FoundBytes), typeof(long), typeof(DiskStrip),
            new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.AffectsRender));

    public long TotalBytes
    {
        get => (long)GetValue(TotalBytesProperty);
        set => SetValue(TotalBytesProperty, value);
    }

    public long UsedBytes
    {
        get => (long)GetValue(UsedBytesProperty);
        set => SetValue(UsedBytesProperty, value);
    }

    public long FoundBytes
    {
        get => (long)GetValue(FoundBytesProperty);
        set => SetValue(FoundBytesProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        base.OnRender(drawingContext);

        var shirina = ActualWidth;
        var vysota = ActualHeight;

        if (shirina <= 0 || vysota <= 0 || TotalBytes <= 0)
        {
            return;
        }

        var kisti = new[]
        {
            Kist("RaisedBrush"),
            Kist("AccentFillBrush"),
            Kist("LineBrush"),
        };

        var shiriny = Razlozhit(Doli(TotalBytes, UsedBytes, FoundBytes), shirina);

        var x = 0.0;
        for (var i = 0; i < shiriny.Length; i++)
        {
            if (shiriny[i] <= 0)
            {
                continue;
            }

            drawingContext.DrawRectangle(
                kisti[i], null, new Rect(x, 0, shiriny[i], vysota));
            x += shiriny[i];
        }
    }

    /// <summary>
    /// Три доли: занято прочим, найдено, свободно. Отделено от рисования, чтобы
    /// арифметику можно было проверить без окна.
    /// </summary>
    /// <remarks>
    /// Найденное это ЧАСТЬ занятого, а не добавка к нему. Нарисовать его
    /// отдельным куском поверх занятого значит показать диск полнее, чем он
    /// есть, и человек увидит, что после очистки освободится больше, чем
    /// освободится.
    /// </remarks>
    internal static double[] Doli(long vsego, long zanyato, long naydeno)
    {
        if (vsego <= 0)
        {
            return [0, 0, 0];
        }

        var zanyatoBezVyhoda = Math.Clamp(zanyato, 0, vsego);
        var naydenoBayt = Math.Clamp(naydeno, 0, zanyatoBezVyhoda);
        var procheeBayt = zanyatoBezVyhoda - naydenoBayt;
        var svobodnoBayt = vsego - zanyatoBezVyhoda;

        return
        [
            (double)procheeBayt / vsego,
            (double)naydenoBayt / vsego,
            (double)svobodnoBayt / vsego,
        ];
    }

    /// <summary>
    /// Turns shares into pixel widths that add up to the track exactly, giving
    /// every non-zero share at least <see cref="MinimalnayaShirina"/> pixels.
    /// </summary>
    internal static double[] Razlozhit(double[] doli, double shirina)
    {
        ArgumentNullException.ThrowIfNull(doli);

        var shiriny = doli.Select(d => d * shirina).ToArray();

        for (var i = 0; i < shiriny.Length; i++)
        {
            if (doli[i] > 0 && shiriny[i] < MinimalnayaShirina)
            {
                var nedostayet = MinimalnayaShirina - shiriny[i];
                shiriny[i] = MinimalnayaShirina;

                // Добираем у самого широкого соседа, а не поровну: равномерный
                // отъём двигает границы всех сегментов сразу, и полоса дёргается
                // целиком там, где изменилась одна десятая процента.
                var donor = Array.IndexOf(shiriny, shiriny.Max());
                if (donor != i)
                {
                    shiriny[donor] -= nedostayet;
                }
            }
        }

        // Хвост округления кладём в последний ненулевой сегмент: иначе справа
        // остаётся щель в пиксель, и на 2560 она видна.
        var summa = shiriny.Sum();
        if (summa > 0 && Math.Abs(summa - shirina) > 0.01)
        {
            var posledniy = Array.FindLastIndex(shiriny, w => w > 0);
            if (posledniy >= 0)
            {
                shiriny[posledniy] += shirina - summa;
            }
        }

        return shiriny;
    }

    /// <summary>
    /// Своё представление в дереве автоматизации.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Без этого полосы В ДЕРЕВЕ НЕТ ВООБЩЕ. <see cref="UIElement.OnCreateAutomationPeer"/>
    /// по умолчанию отдаёт null, и <c>AutomationProperties.AutomationId</c> в
    /// разметке остаётся украшением: некому его нести. Проверено живым прогоном
    /// 05.09.2026, где поиск «disk-strip» не нашёл ничего при совершенно
    /// правильной разметке.
    /// </para>
    /// <para>
    /// Имя собирается из чисел, а не берётся статической подписью: экранный
    /// диктор обязан прочитать то же, что человек видит глазами. Полоса без
    /// чисел это цветной прямоугольник, который нечем озвучить.
    /// </para>
    /// </remarks>
    protected override AutomationPeer OnCreateAutomationPeer() => new PolosaTomaPeer(this);

    private Brush Kist(string klyuch) =>
        (Brush)(TryFindResource(klyuch)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"нет кисти {klyuch}: полоса тома разъехалась с токенами темы")));
}

/// <summary>
/// What a screen reader hears instead of a drawn rectangle.
/// </summary>
/// <remarks>
/// Тип элемента ProgressBar, а не Custom: полоса и есть доля от целого, и
/// средства доступности умеют её так объявлять. Значение не выставляется
/// шаблоном ValuePattern намеренно: доли три, а не одна, и «47 процентов» без
/// уточнения, чего именно, врёт.
/// </remarks>
internal sealed class PolosaTomaPeer(DiskStrip vladelec) : FrameworkElementAutomationPeer(vladelec)
{
    protected override string GetClassNameCore() => nameof(DiskStrip);

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.ProgressBar;

    protected override bool IsControlElementCore() => true;

    protected override string GetNameCore()
    {
        var polosa = (DiskStrip)Owner;

        if (polosa.TotalBytes <= 0)
        {
            return "Полоса тома: данных о диске нет";
        }

        return string.Create(
            CultureInfo.GetCultureInfo("ru-RU"),
            $"Полоса тома: занято {ByteSizeFormatter.Format(Math.Clamp(polosa.UsedBytes, 0, polosa.TotalBytes))} "
            + $"из {ByteSizeFormatter.Format(polosa.TotalBytes)}, "
            + $"найдено {ByteSizeFormatter.Format(Math.Clamp(polosa.FoundBytes, 0, polosa.TotalBytes))}");
    }
}

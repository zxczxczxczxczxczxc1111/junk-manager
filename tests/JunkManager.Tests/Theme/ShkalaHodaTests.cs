using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FluentAssertions;
using JunkManager.Tests.Infrastructure;
using Xunit;

namespace JunkManager.Tests.Theme;

/// <summary>
/// Шкала хода рисует долю, а не сама себя.
/// </summary>
/// <remarks>
/// Шкала это единственное, из чего человек узнаёт, идёт ли долгая работа. Пустая
/// на середине или полная в начале, она не падает и не жалуется: ошибка привязки
/// в WPF молчит. Найдено 06.09.2026 ловушкой привязок, до неё шкалу не проверял
/// никто.
/// </remarks>
// Коллекция ТА ЖЕ, что у сборки окна, и это не про скорость. Application в
// домене существует ровно один, создаётся в первом же потоке STA и навсегда
// принадлежит ему: свой IClassFixture поднял бы второй поток и второй набор
// ресурсов. Довод целиком записан на StaStend.
[Trait("Class", "Sandbox")]
[Collection(StaNabor.Imya)]
public sealed class ShkalaHodaTests(StaStend stend)
{

    [Theory]
    [InlineData(0d)]
    [InlineData(0.25d)]
    [InlineData(0.5d)]
    [InlineData(1d)]
    public void Zalivka_shkaly_shirinoy_v_dolyu_dorozhki(double dolya)
    {
        stend.Vypolnit(() =>
        {
            var shkala = new ProgressBar { Width = 400, Height = 8, Value = dolya };

            using var lovushka = new LovushkaPrivyazok();
            using var istochnik = PodnyatV(shkala);

            var zalivka = NaytiPoImeni(shkala, "Determinate");
            var dorozhka = NaytiPoImeni(shkala, "Track");

            zalivka.Should().NotBeNull("шаблон шкалы обязан содержать заливку");
            dorozhka.Should().NotBeNull("шаблон шкалы обязан содержать дорожку");

            // Доля берётся от НАСТОЯЩЕЙ дорожки, а не от заданной ширины:
            // рамка в один пиксель с каждой стороны съедает два, и числа вроде
            // «400» в ожидании были бы враньём про рамку, а не про шкалу.
            ShirinaZalivki(zalivka!).Should().BeApproximately(dolya * dorozhka!.ActualWidth, 0.5);

            lovushka.Oshibki.Should().BeEmpty("шкала не имеет права ругаться в трассировку");
        });
    }

    /// <summary>
    /// Насколько заливка реально закрывает дорожку. Читается и ширина, и
    /// масштаб: заливка может быть шириной в дорожку и сжата преобразованием,
    /// и наоборот.
    /// </summary>
    private static double ShirinaZalivki(FrameworkElement zalivka)
    {
        var masshtab = zalivka.RenderTransform is ScaleTransform s ? s.ScaleX : 1d;
        return zalivka.ActualWidth * masshtab;
    }

    private static FrameworkElement? NaytiPoImeni(DependencyObject koren, string imya)
    {
        if (koren is FrameworkElement element
            && string.Equals(element.Name, imya, StringComparison.Ordinal))
        {
            return element;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(koren); i++)
        {
            var nashlos = NaytiPoImeni(VisualTreeHelper.GetChild(koren, i), imya);

            if (nashlos is not null)
            {
                return nashlos;
            }
        }

        return null;
    }

    /// <summary>
    /// Невидимое окно с настоящей раскладкой. Без него ActualWidth равен нулю, и
    /// проверка сравнивала бы нули с нулями.
    /// </summary>
    private static System.Windows.Interop.HwndSource PodnyatV(FrameworkElement koren)
    {
        var parametry = new System.Windows.Interop.HwndSourceParameters("shkala-proverka")
        {
            Width = 600,
            Height = 200,
            WindowStyle = unchecked((int)0x80000000),
            PositionX = -32000,
            PositionY = -32000,
        };

        var istochnik = new System.Windows.Interop.HwndSource(parametry) { RootVisual = koren };

        koren.UpdateLayout();
        koren.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        koren.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        return istochnik;
    }
}

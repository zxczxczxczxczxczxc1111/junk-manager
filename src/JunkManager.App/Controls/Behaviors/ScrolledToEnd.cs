using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace JunkManager.App.Controls.Behaviors;

/// <summary>
/// Reports whether a scroll viewer has been read to the bottom.
/// </summary>
/// <remarks>
/// The delete button on the confirmation step is bound to this. It is not a
/// nag: the list is the thing being agreed to, and a button that can be pressed
/// without the list ever moving turns a full preview back into a number.
/// </remarks>
internal static class ScrolledToEnd
{
    /// <summary>Допуск в пикселях. Прокрутка по строкам оставляет дробный хвост.</summary>
    private const double Dopusk = 2;

    public static readonly DependencyProperty WatchProperty =
        DependencyProperty.RegisterAttached(
            "Watch", typeof(bool), typeof(ScrolledToEnd),
            new PropertyMetadata(false, OnWatchChanged));

    public static readonly DependencyProperty ReachedProperty =
        DependencyProperty.RegisterAttached(
            "Reached", typeof(bool), typeof(ScrolledToEnd),
            new FrameworkPropertyMetadata(
                false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static void SetWatch(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(WatchProperty, value);
    }

    public static bool GetWatch(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(WatchProperty);
    }

    public static void SetReached(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetCurrentValue(ReachedProperty, value);
        BindingOperations.GetBindingExpression(element, ReachedProperty)?.UpdateSource();
    }

    public static bool GetReached(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(ReachedProperty);
    }

    /// <summary>
    /// The whole rule, as a function of four numbers so it can be tested without
    /// a window.
    /// </summary>
    /// <returns>
    /// False for an empty list. Confirmation with nothing selected is a defect,
    /// and a button that comes alive there would let it through.
    /// </returns>
    public static bool Dostignut(double offset, double viewport, double extent, double dopusk)
    {
        if (extent <= 0)
        {
            return false;
        }

        return extent <= viewport || offset + viewport >= extent - dopusk;
    }

    /// <remarks>
    /// Вешается на ЛЮБОЙ элемент, а не только на <see cref="ScrollViewer"/>:
    /// <c>ScrollChanged</c> это всплывающее событие, поэтому список со своей
    /// прокруткой внутри шаблона обслуживается тем же кодом. Иначе пришлось бы
    /// либо лезть в шаблон списка за его собственным ScrollViewer, либо
    /// оборачивать список в чужую прокрутку и потерять виртуализацию.
    /// </remarks>
    private static void OnWatchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var obrabotchik = new ScrollChangedEventHandler(Prokrutilos);

        if (e.NewValue is true)
        {
            // Событие приходит и на первой раскладке, когда меняется размер
            // содержимого. Поэтому короткий список, который не прокрутить
            // вовсе, объявляется долистанным сам, без отдельного пересчёта.
            element.AddHandler(ScrollViewer.ScrollChangedEvent, obrabotchik);
            element.Loaded += OnLoaded;
            element.IsVisibleChanged += OnVisibleChanged;
        }
        else
        {
            element.RemoveHandler(ScrollViewer.ScrollChangedEvent, obrabotchik);
            element.Loaded -= OnLoaded;
            element.IsVisibleChanged -= OnVisibleChanged;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Refresh((FrameworkElement)sender);

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh((FrameworkElement)sender);

    private static void Refresh(FrameworkElement element)
    {
        if (!element.IsVisible) { SetReached(element, false); return; }
        _ = element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var viewer = FindViewer(element);
            SetReached(element, element.IsVisible && viewer is not null && viewer.ViewportHeight > 0
                && Dostignut(viewer.VerticalOffset, viewer.ViewportHeight, viewer.ExtentHeight, Dopusk));
        });
    }

    private static ScrollViewer? FindViewer(DependencyObject element)
    {
        if (element is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var found = FindViewer(VisualTreeHelper.GetChild(element, index));
            if (found is not null) return found;
        }
        return null;
    }

    private static void Prokrutilos(object sender, ScrollChangedEventArgs e) =>
        SetReached((DependencyObject)sender, sender is FrameworkElement { IsVisible: true }
            && e.ViewportHeight > 0 && Dostignut(e.VerticalOffset, e.ViewportHeight, e.ExtentHeight, Dopusk));
}

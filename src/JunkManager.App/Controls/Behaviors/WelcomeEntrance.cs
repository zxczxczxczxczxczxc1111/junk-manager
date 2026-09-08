using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace JunkManager.App.Controls.Behaviors;

/// <summary>One visible entrance, after the first layout has stopped eating frames.</summary>
internal static class WelcomeEntrance
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(WelcomeEntrance), new PropertyMetadata(false, OnEnabledChanged));
    private static readonly DependencyProperty PlayedProperty = DependencyProperty.RegisterAttached(
        "Played", typeof(bool), typeof(WelcomeEntrance), new PropertyMetadata(false));

    public static bool GetEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(EnabledProperty);
    }

    public static void SetEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(EnabledProperty, value);
    }

    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement element) return;
        element.Loaded -= OnLoaded;
        if (args.NewValue is true)
        {
            element.Loaded += OnLoaded;
            if (element.IsLoaded) QueueEntrance(element);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs args) => QueueEntrance((FrameworkElement)sender);

    private static void QueueEntrance(FrameworkElement element)
    {
        var window = Window.GetWindow(element);
        if (window is null || (bool)window.GetValue(PlayedProperty) || !element.IsVisible) return;
        // Read the live policy; a StaticResource can remember yesterday's enthusiasm.
        if (element.TryFindResource("MotionEnabled") is not true)
        {
            window.SetValue(PlayedProperty, true);
            return;
        }
        element.Opacity = 0;
        _ = element.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            element.Opacity = 1;
            if (!element.IsLoaded || !element.IsVisible || !GetEnabled(element)
                || (bool)window.GetValue(PlayedProperty)) return;
            window.SetValue(PlayedProperty, true);
            var duration = new Duration(TimeSpan.FromMilliseconds(650));
            var offset = new TranslateTransform();
            element.RenderTransform = offset;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(20, 0, duration)
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        });
    }
}

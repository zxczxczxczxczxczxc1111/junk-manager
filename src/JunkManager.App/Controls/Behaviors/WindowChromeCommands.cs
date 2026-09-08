using System.Windows;
using System.Windows.Input;

namespace JunkManager.App.Controls.Behaviors;

/// <summary>
/// Minimise, maximise and close as attached commands, so a custom title bar
/// does not need code-behind. Without this the three caption buttons are the
/// one honest reason to put a Click handler in a view, and one reason becomes
/// three within a month.
/// </summary>
internal static class WindowChromeCommands
{
    public static readonly RoutedUICommand Minimize =
        new("Свернуть", nameof(Minimize), typeof(WindowChromeCommands));

    public static readonly RoutedUICommand ToggleMaximize =
        new("Развернуть", nameof(ToggleMaximize), typeof(WindowChromeCommands));

    public static readonly RoutedUICommand Close =
        new("Закрыть", nameof(Close), typeof(WindowChromeCommands));

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(WindowChromeCommands),
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

        okno.CommandBindings.Add(new CommandBinding(
            Minimize, (_, _) => okno.WindowState = WindowState.Minimized));

        okno.CommandBindings.Add(new CommandBinding(
            ToggleMaximize, (_, _) => okno.WindowState =
                okno.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized));

        okno.CommandBindings.Add(new CommandBinding(
            Close, (_, _) => okno.Close()));
    }
}

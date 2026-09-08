using System.Windows;
using System.Windows.Controls;
using JunkManager.App.ViewModels;

namespace JunkManager.App.Controls;

/// <summary>
/// One control that decides what a screen shows. Screens set <see cref="Phase"/>
/// and <see cref="State"/> and nothing else: the four state layouts come from
/// the implicit style, so a new screen cannot forget to draw one of them.
/// </summary>
/// <remarks>
/// <see cref="State"/> is typed object and not the view model, deliberately.
/// Controls must not know view model types: the templates bind by property name
/// and the boundary stays where the architecture test can see it.
/// </remarks>
internal sealed class StateHost : ContentControl
{
    public static readonly DependencyProperty PhaseProperty =
        DependencyProperty.Register(
            nameof(Phase), typeof(ScreenPhase), typeof(StateHost),
            new FrameworkPropertyMetadata(ScreenPhase.Empty, PriSmeneFazy));

    static StateHost()
    {
        // Хост берёт фокус на себя, но в обход Tab: это не элемент управления,
        // а место, куда фокус можно деть. Своей рамки у него нет по той же
        // причине.
        FocusableProperty.OverrideMetadata(
            typeof(StateHost), new FrameworkPropertyMetadata(true));
        IsTabStopProperty.OverrideMetadata(
            typeof(StateHost), new FrameworkPropertyMetadata(false));
        FocusVisualStyleProperty.OverrideMetadata(
            typeof(StateHost), new FrameworkPropertyMetadata((object?)null));
    }

    /// <summary>
    /// Надо ли забрать фокус себе при смене фазы.
    /// </summary>
    /// <remarks>
    /// Проверено запуском 05.09.2026: кнопка «Сканировать диск» в пустом
    /// состоянии, нажатая с клавиатуры, оставляет за собой ЛИЛОВУЮ РАМКУ ПОСЕРЕДИ
    /// ЭКРАНА. Фаза уходит в Loading, презентер схлопывается, но клавиатурный
    /// фокус остаётся на невидимой кнопке, а её рамка живёт в слое украшений и
    /// про Visibility ничего не знает.
    ///
    /// Мешает это не только глазам: Enter на пустом месте запускает скрытую
    /// кнопку заново, а Tab начинает обход из ниоткуда.
    ///
    /// Отдельная функция, а не строчка в обработчике: условие проверяется
    /// тестом, а поднимать ради него окно с настоящим фокусом дорого и
    /// ненадёжно.
    /// </remarks>
    internal static bool NuzhnoZabratFokus(bool fokusVnutri, ScreenPhase byla, ScreenPhase stala) =>
        fokusVnutri && byla != stala;

    private static void PriSmeneFazy(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (StateHost)d;

        if (NuzhnoZabratFokus(
            host.IsKeyboardFocusWithin, (ScreenPhase)e.OldValue, (ScreenPhase)e.NewValue))
        {
            host.Focus();
        }
    }

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State), typeof(object), typeof(StateHost),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty HasRestrictionProperty =
        DependencyProperty.Register(
            nameof(HasRestriction), typeof(bool), typeof(StateHost),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty EmptyTemplateProperty =
        DependencyProperty.Register(
            nameof(EmptyTemplate), typeof(DataTemplate), typeof(StateHost),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty LoadingTemplateProperty =
        DependencyProperty.Register(
            nameof(LoadingTemplate), typeof(DataTemplate), typeof(StateHost),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ErrorTemplateProperty =
        DependencyProperty.Register(
            nameof(ErrorTemplate), typeof(DataTemplate), typeof(StateHost),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty RestrictionTemplateProperty =
        DependencyProperty.Register(
            nameof(RestrictionTemplate), typeof(DataTemplate), typeof(StateHost),
            new FrameworkPropertyMetadata(null));

    public ScreenPhase Phase
    {
        get => (ScreenPhase)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    public object? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool HasRestriction
    {
        get => (bool)GetValue(HasRestrictionProperty);
        set => SetValue(HasRestrictionProperty, value);
    }

    public DataTemplate? EmptyTemplate
    {
        get => (DataTemplate?)GetValue(EmptyTemplateProperty);
        set => SetValue(EmptyTemplateProperty, value);
    }

    public DataTemplate? LoadingTemplate
    {
        get => (DataTemplate?)GetValue(LoadingTemplateProperty);
        set => SetValue(LoadingTemplateProperty, value);
    }

    public DataTemplate? ErrorTemplate
    {
        get => (DataTemplate?)GetValue(ErrorTemplateProperty);
        set => SetValue(ErrorTemplateProperty, value);
    }

    public DataTemplate? RestrictionTemplate
    {
        get => (DataTemplate?)GetValue(RestrictionTemplateProperty);
        set => SetValue(RestrictionTemplateProperty, value);
    }
}

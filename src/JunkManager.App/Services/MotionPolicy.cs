using System.Windows;

namespace JunkManager.App.Services;

/// <summary>
/// Honours the system "reduce motion" setting by rewriting the two duration
/// tokens once at startup.
/// </summary>
/// <remarks>
/// Rewriting the tokens beats checking a flag inside every storyboard: there
/// are dozens of storyboards and one place to forget. Reduced motion means
/// instant, not slow: an animation stretched to zero still ends where it was
/// going, and every trigger keeps working unchanged.
/// </remarks>
internal static class MotionPolicy
{
    private static readonly Duration Mgnovenno = new(TimeSpan.Zero);

    private static readonly string[] Klyuchi = ["MotionFast", "MotionMedium"];

    /// <summary>
    /// What Windows says about animation in client areas. Read once at startup;
    /// a person who changes it mid-session restarts the application, and polling
    /// it costs a system call on every trigger.
    /// </summary>
    public static bool SystemAllowsAnimation => SystemParameters.ClientAreaAnimation;

    public static void Apply(ResourceDictionary resources, bool animationsAllowed)
    {
        ArgumentNullException.ThrowIfNull(resources);

        foreach (var klyuch in Klyuchi)
        {
            // Contains смотрит и во вложенные словари, поэтому проверка честна
            // и для Application.Resources, где своих записей нет вовсе.
            if (!resources.Contains(klyuch))
            {
                throw new InvalidOperationException(
                    $"в словаре нет длительности {klyuch}: токены темы разъехались с политикой движения");
            }
        }

        resources["MotionEnabled"] = animationsAllowed;
        if (animationsAllowed)
        {
            return;
        }

        foreach (var klyuch in Klyuchi)
        {
            // Запись ложится в ВЕРХНИЙ словарь и перекрывает вложенный: поиск
            // сначала смотрит свои записи и только потом объединённые.
            resources[klyuch] = Mgnovenno;
        }
    }
}

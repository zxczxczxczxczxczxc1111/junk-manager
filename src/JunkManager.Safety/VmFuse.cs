namespace JunkManager.Safety;

/// <summary>
/// Gate for destructive tests. Two independent conditions, and one is never
/// enough: an environment variable gets set by a stray script, a marker file
/// gets restored by a stray backup. Both at once only happens on purpose.
/// </summary>
public static class VmFuse
{
    public const string EnvName = "JUNKMANAGER_TEST_VM";

    public const string MarkerPath = @"C:\junkmanager-test-vm.marker";

    /// <summary>
    /// The polygon's computer name, set by polygon/autounattend.xml. It exists so
    /// tests can state where the fuse is supposed to be armed and where it is not.
    /// It is deliberately NOT part of <see cref="Evaluate"/>: a fuse that trusts a
    /// machine name is a fuse anybody can forge by renaming a machine.
    /// </summary>
    public const string PolygonComputerName = "JM-STEND";

    /// <summary>Reads the real machine.</summary>
    public static bool IsArmed =>
        Evaluate(Environment.GetEnvironmentVariable(EnvName), File.Exists(MarkerPath));

    /// <summary>
    /// The whole decision, with nothing ambient in it, so tests never have to
    /// touch the real environment or drop a marker file on a developer machine
    /// to exercise the logic. Exact string match on purpose: trimming or
    /// truthiness would widen the gate without anyone noticing.
    /// </summary>
    private static bool Evaluate(string? envValue, bool markerExists) =>
        string.Equals(envValue, "1", StringComparison.Ordinal) && markerExists;

    public static void RequireArmed() =>
        RequireArmed(Environment.GetEnvironmentVariable(EnvName), File.Exists(MarkerPath));

    /// <summary>
    /// Throws unless both conditions hold. The message names both conditions and
    /// their current state: a refusal that does not say what is missing turns
    /// into a bug report instead of a fix.
    /// </summary>
    public static void RequireArmed(string? envValue, bool markerExists)
    {
        if (Evaluate(envValue, markerExists))
        {
            return;
        }

        var envState = envValue is null ? "не задана" : $"'{envValue}'";
        var markerState = markerExists ? "есть" : "нет";

        throw new InvalidOperationException(
            $"Разрушительная операция запрещена: предохранитель не взведён. " +
            $"Нужны ОБА условия сразу: переменная {EnvName}=1 и файл {MarkerPath}. " +
            $"Сейчас переменная {envState}, маркер {markerState}.");
    }
}

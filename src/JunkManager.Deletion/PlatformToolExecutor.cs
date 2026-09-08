using JunkManager.Core.Sources.Platform;
using JunkManager.Safety;

namespace JunkManager.Deletion;

/// <summary>
/// The only place that runs the destructive half of DISM and pnputil. Both
/// commands are assembled from the same builders the analysis uses, so the
/// "/ResetBase never appears" test covers this path too rather than a copy of it.
/// </summary>
/// <remarks>
/// <para>
/// Предохранителя ВМ здесь БОЛЬШЕ НЕТ, решение владельца 06.09.2026, тем же
/// решением, что и у обработчиков очистки. Находка платформенной утилиты
/// показывается человеку со ступенью риска и последствием и удаляется только
/// после подтверждения, как всё остальное.
/// </para>
/// <para>
/// Ворота, которые остались: сборка команд из тех же строителей, что и разбор,
/// то есть `/ResetBase` не появляется никогда и `/force` с `/uninstall` не
/// появляются никогда; проверка имени пакета до запуска процесса; и терпение в
/// тридцать минут, после которого утилита считается зависшей.
/// </para>
/// </remarks>
public static class PlatformToolExecutor
{
    private static readonly TimeSpan Terpenie = TimeSpan.FromMinutes(30);

    public static Task<ToolRun> CleanComponentStoreAsync(CancellationToken ct) =>
        ProcessRunner.RunAsync(
            ProcessRunner.SystemTool("dism.exe"),
            DismComponentStore.CleanupArguments(),
            Terpenie,
            ct);

    public static Task<ToolRun> DeleteDriverAsync(string publishedName, CancellationToken ct)
    {
        // Имя проверяется ДО запуска процесса и остаётся единственными воротами
        // этого метода: кривое имя это дефект вызывающего, и запускать с ним
        // pnputil нельзя даже ради того, чтобы посмотреть, что он ответит.
        var arguments = PnpDriverStore.DeleteArguments(publishedName);

        return ProcessRunner.RunAsync(
            ProcessRunner.SystemTool("pnputil.exe"), arguments, Terpenie, ct);
    }
}

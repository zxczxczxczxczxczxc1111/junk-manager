using System.Diagnostics.CodeAnalysis;
using JunkManager.Core.Interop;
using JunkManager.Safety;

namespace JunkManager.Deletion;

/// <summary>
/// Runs VACUUM in place. Lives in Deletion despite deleting nothing, because it
/// rewrites a file the user cares about and a failed rewrite of a browser
/// history is indistinguishable from losing it.
/// </summary>
public static class SqliteVacuumExecutor
{
    /// <param name="freedBytes">Counted, never forecast: the size before minus the size after.</param>
    /// <param name="reason">Null only on success, filled on every refusal.</param>
    public static bool TryCompact(
        VerifiedPath path, out long freedBytes, [NotNullWhen(false)] out string? reason)
    {
        freedBytes = 0;

        // Re-verify against the resolved path: between the scan that measured
        // this database and this call, the name could have been pointed at
        // something else entirely.
        if (!SafetyGuard.TryVerifyForDeletion(path.Value, out var checkedPath, out reason))
        {
            return false;
        }

        long before;
        try
        {
            before = new FileInfo(checkedPath.Value).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = $"файл не читается: {ex.Message}";
            return false;
        }

        var estimate = SqliteCompactor.Measure(checkedPath.Value);
        if (estimate.Failure is not null) { reason = estimate.Failure; return false; }

        var rc = WinSqlite.TryOpenExisting(checkedPath.Value, out var db);

        if (rc != WinSqlite.Ok)
        {
            _ = WinSqlite.Close(db);
            reason = rc == WinSqlite.CantOpen
                ? "база занята другим процессом"
                : $"sqlite3_open_v2 вернул {WinSqlite.Opisat(rc)}";
            return false;
        }

        try
        {
            // An implicit ROWID can change during VACUUM. Silent identity surgery is not cache cleanup.
            const string unsupportedSchema = """
                SELECT count(*) FROM pragma_table_list AS t
                WHERE t.schema='main' AND t.name NOT LIKE 'sqlite_%' AND t.type<>'view'
                  AND (t.type<>'table' OR (t.wr=0 AND (
                    (SELECT count(*) FROM pragma_table_info(t.name) WHERE pk>0)<>1 OR
                    (SELECT count(*) FROM pragma_table_info(t.name) WHERE pk=1 AND upper(type)='INTEGER')<>1 OR
                    (SELECT count(*) FROM pragma_index_list(t.name) WHERE origin='pk')<>0)))
                """;
            if (WinSqlite.ScalarInt64(db, unsupportedSchema) != 0)
            {
                reason = "схема содержит неявные идентификаторы или неподдерживаемые таблицы; база сохранена";
                return false;
            }
            if (WinSqlite.Exec(db, "VACUUM;", out var error) != WinSqlite.Ok)
            {
                reason = $"сжатие не удалось: {error ?? "неизвестная ошибка sqlite"}";
                return false;
            }
        }
        finally
        {
            _ = WinSqlite.Close(db);
        }

        try
        {
            freedBytes = Math.Max(0, before - new FileInfo(checkedPath.Value).Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The compaction already happened, so this is not a refusal: it is a
            // success whose gain could not be counted. Reporting zero freed is
            // the honest answer, and it is better than claiming a number.
            System.Diagnostics.Trace.TraceWarning(
                $"размер после сжатия не прочитан: {ex.Message}");
            freedBytes = 0;
        }

        reason = null;
        return true;
    }
}

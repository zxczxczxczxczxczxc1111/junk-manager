using JunkManager.Core.Registry;
using JunkManager.Safety;

namespace JunkManager.Deletion;

/// <param name="Address">
/// Адрес записи, которой сейчас занимаемся. Освобождённых байт здесь нет и не
/// будет: раздел 9 спеки запрещает показывать их для реестра, потому что их там
/// нет, а поле, которое всегда ноль, однажды кто-нибудь нарисует.
/// </param>
public sealed record RegistryCleanupProgress(
    int Done, int Total, string Address, DeleteOutcome? Last = null)
{
    public double Share => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : 0;
}

/// <param name="Cancelled">
/// True when the person stopped it. Without the flag a run stopped after two of
/// two hundred looks exactly like a small successful one.
/// </param>
public sealed record RegistryCleanupReport(
    IReadOnlyList<DeleteOutcome> Outcomes, bool Cancelled)
{
    public int DeletedCount => Outcomes.Count(o => o.Status == DeleteStatus.Deleted);

    public int SkippedCount => Outcomes.Count(o => o.Status == DeleteStatus.Skipped);

    public int FailedCount => Outcomes.Count(o => o.Status == DeleteStatus.Failed);

    public int CancelledCount => Outcomes.Count(o => o.Status == DeleteStatus.Cancelled);
}

/// <summary>
/// Runs a batch of registry findings through <see cref="RegistryExecutor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The routing between a value and a key lives here and nowhere else. It used to
/// live in the CLI, and the moment the window needed it too there would have
/// been two answers to "how is a registry finding removed". Two answers drift,
/// and this one drifts into deleting a whole key where one string was promised.
/// </para>
/// <para>
/// The guard is asked again here even though the scanner already asked. Findings
/// reach this method from a screen, from the CLI and from tests, and this is the
/// last place where saying no is still possible.
/// </para>
/// </remarks>
public sealed class RegistryCleanupRunner
{
    private readonly RegistryExecutor _ispolnitel;

    public RegistryCleanupRunner(RegistryExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _ispolnitel = executor;
    }

    public async Task<RegistryCleanupReport> RunAsync(
        IReadOnlyList<RegistryFinding> findings,
        IProgress<RegistryCleanupProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var ishody = new List<DeleteOutcome>(findings.Count);
        var otmeneno = false;

        foreach (var nahodka in findings)
        {
            if (ct.IsCancellationRequested)
            {
                // Недошедшие находки исхода не получают вовсе: строка
                // «отменено» на каждую это журнал про то, чего не было.
                otmeneno = true;
                break;
            }

            progress?.Report(new RegistryCleanupProgress(
                ishody.Count, findings.Count, nahodka.Address));

            var ishod = await Ubrat(nahodka, ct).ConfigureAwait(false);
            ishody.Add(ishod);

            progress?.Report(new RegistryCleanupProgress(
                ishody.Count, findings.Count, nahodka.Address, ishod));
        }

        return new RegistryCleanupReport(ishody, otmeneno || ct.IsCancellationRequested
            || ishody.Any(outcome => outcome.Status == DeleteStatus.Cancelled));
    }

    private async Task<DeleteOutcome> Ubrat(RegistryFinding nahodka, CancellationToken ct)
    {
        var allowed = nahodka.Kind == RegistryEntryKind.Value
            ? RegistryGuard.TryVerifyValue(nahodka.Hive, nahodka.SubKey, nahodka.ValueName, nahodka.View, out _, out var refusal)
            : RegistryGuard.TryVerifyKey(nahodka.Hive, nahodka.SubKey, nahodka.View, out _, out refusal);
        if (!allowed) return await Skip(nahodka.Address, refusal).ConfigureAwait(false);
        RegistryEntrySnapshot? snapshot;
        try
        {
            snapshot = RegistryEntrySnapshot.Capture(nahodka.Hive, nahodka.SubKey, nahodka.ValueName,
                nahodka.View, nahodka.Kind == RegistryEntryKind.Key);
            if (snapshot is null)
                return await Skip(nahodka.Address, "запись уже отсутствует").ConfigureAwait(false);
            if (nahodka.Snapshot is not null ? !nahodka.Snapshot.Matches(snapshot)
                : !snapshot.Values.Any(value => value.RelativeKey.Length == 0
                    && value.Name.Equals(nahodka.ValueName, StringComparison.OrdinalIgnoreCase)
                    && value.Decode() is string raw && raw.Equals(nahodka.RawValue, StringComparison.Ordinal)))
                return await Skip(nahodka.Address, "запись изменилась после сканирования").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return await Skip(nahodka.Address, "повторная проверка недоступна: " + ex.Message).ConfigureAwait(false);
        }
        if (nahodka.Kind == RegistryEntryKind.Value)
        {
            if (!RegistryGuard.TryVerifyValue(
                    nahodka.Hive, nahodka.SubKey, nahodka.ValueName, nahodka.View,
                    out var propusk, out var otkaz))
            {
                return await Skip(nahodka.Address, otkaz).ConfigureAwait(false);
            }

            return await _ispolnitel.DeleteValueAsync(propusk, snapshot, nahodka.MissingTarget, ct).ConfigureAwait(false);
        }

        if (!RegistryGuard.TryVerifyKey(
                nahodka.Hive, nahodka.SubKey, nahodka.View,
                out var propuskKlyucha, out var otkazKlyucha))
        {
            return await Skip(nahodka.Address, otkazKlyucha).ConfigureAwait(false);
        }

        return await _ispolnitel.DeleteKeyAsync(propuskKlyucha, snapshot, nahodka.MissingTarget, ct).ConfigureAwait(false);
    }

    private Task<DeleteOutcome> Skip(string address, string? reason) =>
        _ispolnitel.RecordAsync(new DeleteOutcome(address, DeleteStatus.Skipped, 0, reason));
}

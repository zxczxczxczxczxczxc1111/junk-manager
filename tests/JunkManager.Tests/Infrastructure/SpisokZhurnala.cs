using JunkManager.Deletion;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Журнал, который никуда не пишет и всё помнит. Нужен, чтобы проверять ЧТО
/// записано, не разбирая файл: формат файла проверяется отдельно, в
/// OperationLogTests, и смешивать эти два вопроса в одном тесте значит не
/// проверить толком ни одного.
/// </summary>
public sealed class SpisokZhurnala : IOperationLog
{
    private readonly List<DeleteOutcome> _zapisi = [];

    public IReadOnlyList<DeleteOutcome> Zapisi => _zapisi;

    public Task RecordAsync(DeleteOutcome outcome, CancellationToken ct = default)
    {
        _zapisi.Add(outcome);
        return Task.CompletedTask;
    }
}

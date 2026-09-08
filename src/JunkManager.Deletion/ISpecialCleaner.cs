using JunkManager.Core;

namespace JunkManager.Deletion;

/// <summary>
/// Removes a finding whose <see cref="Finding.Path"/> is an identity, not a
/// location on disk.
/// </summary>
/// <remarks>
/// Интерфейс, а не статический вызов, ровно по одной причине: за ним стоят
/// необратимые операции чужими руками (COM-обработчик Windows, DISM, pnputil),
/// и проверка перебора обязана уметь спросить «кому ты это отдал», не запуская
/// ни одну из них.
/// </remarks>
public interface ISpecialCleaner
{
    Task<DeleteOutcome> CleanAsync(Finding finding, CancellationToken ct);
}

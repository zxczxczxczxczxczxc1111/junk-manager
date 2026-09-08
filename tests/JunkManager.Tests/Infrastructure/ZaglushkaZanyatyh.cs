using System.Collections.ObjectModel;
using System.Threading;
using JunkManager.App.Services;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// A locked-file service that closes nothing and remembers everything.
/// </summary>
/// <remarks>
/// <para>
/// Общая на весь набор, потому что настоящая служба ЗАКРЫВАЕТ и ЗАВЕРШАЕТ чужие
/// программы человека, а первый её выход продукт зовёт сам, без нажатия. Модель
/// экрана «Файлы» требует службу обязательным параметром ровно ради этого: без
/// подставной проверка сборки окна с занятым файлом просила бы закрыться
/// настоящие программы на машине, где её запустили.
/// </para>
/// <para>
/// По умолчанию отвечает согласием: «попросили и получилось». Отказ ставится
/// через <see cref="Otvet"/> там, где проверяется именно он.
/// </para>
/// </remarks>
internal sealed class ZaglushkaZanyatyh : ILockedFileService
{
    public LockedFileActionResult Otvet { get; set; } = new(true, "готово");

    public Collection<string> Poprosheno { get; } = [];

    public Collection<string> Otlozheno { get; } = [];

    public Collection<string> Zaversheno { get; } = [];

    public Task<LockedFileActionResult> PoprositZakrytsyaAsync(string put, CancellationToken ct)
    {
        Poprosheno.Add(put);
        return Task.FromResult(Otvet);
    }

    public Task<LockedFileActionResult> OtlozhitNaZagruzkuAsync(string put, CancellationToken ct)
    {
        Otlozheno.Add(put);
        return Task.FromResult(Otvet);
    }

    public Task<LockedFileActionResult> ZavershitAsync(string put, CancellationToken ct)
    {
        Zaversheno.Add(put);
        return Task.FromResult(Otvet);
    }
}

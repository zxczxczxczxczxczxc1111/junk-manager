using System.IO;
using JunkManager.Core.Registry;
using JunkManager.Core.Rules;

namespace JunkManager.App.Services;

/// <summary>
/// Встроенные ветки реестра, затем настоящий сканер ядра.
/// </summary>
/// <remarks>
/// <para>
/// Ветки грузятся в конструкторе, а не при каждом нажатии: файл один, читается
/// он мгновенно, а сломанный файл обязан сказать о себе при запуске окна, а не
/// при первом нажатии кнопки. Ровно так же устроен <c>ScanService</c>.
/// </para>
/// <para>
/// Внешние файлы не участвуют в выборе веток. Каталог встроен в Core.
/// </para>
/// </remarks>
internal sealed class RegistryScanService : IRegistryScanService
{
    private readonly IReadOnlyList<RegistryScanRule> _vetki;

    public RegistryScanService()
    {
        // Fail-closed: RegistryScanRules.Load бросает RuleFormatException на
        // битом файле, на ветке без объяснения и на ветке, которую не пропускает
        // guard. Ловит это App.xaml.cs и подставляет SlomannyyReestr: молча
        // пропущенная ветка выглядит на экране как чистый реестр.
        _vetki = BuiltInCatalog.LoadRegistryRules();
    }

    public int BranchCount => _vetki.Count;

    public Task<RegistryScanResult> ScanAsync(IProgress<string>? hod, CancellationToken ct) =>
        new RegistryScanner().ScanAsync(_vetki, hod, ct);
}

/// <summary>
/// Реестр, проверять который нечем: файл веток не прочитан.
/// </summary>
/// <remarks>
/// Пустой список отдавать нельзя ни при каких обстоятельствах. Пустой список
/// означает «посмотрели, ничего нет», а тут не смотрели вовсе, и разница между
/// этими двумя ответами и есть весь смысл экрана.
/// </remarks>
internal sealed class SlomannyyReestr : IRegistryScanService
{
    public int BranchCount => 0;

    public Task<RegistryScanResult> ScanAsync(IProgress<string>? hod, CancellationToken ct) =>
        throw new IOException(
            "встроенный каталог веток реестра не прочитан, проверять нечем");
}

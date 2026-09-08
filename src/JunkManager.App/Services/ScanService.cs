using JunkManager.Core;
using JunkManager.Core.Rules;
using JunkManager.Core.Scanning;

namespace JunkManager.App.Services;

/// <summary>
/// Rules embedded in Core, then the whole pass.
/// </summary>
/// <remarks>
/// <para>
/// Отступление от плана, записано 05.09.2026. План звал <c>FileScanner</c> и
/// только его. С тех пор появился <see cref="PolnyyProhod"/>, и разница не
/// косметическая: на живой машине правила дают 36 находок, а полный проход 72
/// четырьмя источниками. Окно, зовущее один источник из шести, показывало бы
/// половину мусора и называло бы это полным осмотром.
/// </para>
/// <para>
/// Поиск по образцу выключен, как и в CLI по умолчанию: маска не знает, чем
/// занят файл, и всё найденное ею рискованное. Включать его будет человек в
/// настройках, задача 12.
/// </para>
/// <para>
/// Проход отчитывается о ходе строкой: именем источника и путём, без числа
/// шагов. Поэтому доля наверх уходит null и полоса рисуется неопределённой. Это
/// не заглушка: сколько кандидатов у правила, известно только после его
/// раскрытия, а доля, придуманная из числа правил, двигалась бы рывками, не
/// связанными с оставшейся работой.
/// </para>
/// </remarks>
internal sealed class ScanService : IScanService
{
    public ScanService(ScanPlan? plan = null)
    {
        Plan = plan ?? ScanPlan.PoUmolchaniyu;

        // Правила грузятся сразу и намеренно: их число нужно «Обзору» для
        // оценки длительности прохода ещё до первого прохода, а каталог, не
        // доехавший до выкладки, обязан сказать об этом при запуске, а не при
        // первом нажатии кнопки.
        var pravila = BuiltInCatalog.LoadRules();
        RulesCount = pravila.Count;

        // Одно правило может встретиться в одном файле один раз: id уникален по
        // всем файлам, это проверяет загрузчик. Поэтому словарь строится прямо,
        // без разрешения столкновений.
        CategoryByRule = pravila.ToDictionary(
            r => r.Id, r => r.Category, StringComparer.Ordinal);
    }

    /// <summary>
    /// Какие источники участвуют в следующем проходе.
    /// </summary>
    /// <remarks>
    /// Меняется на ходу: настройки «Обнаружители» и «Следы программ» правят
    /// именно его. Плана, зашитого в конструктор, хватило бы только до первого
    /// переключения, а дальше настройка вступала бы в силу со следующего
    /// запуска, то есть выглядела бы сломанной.
    /// </remarks>
    public ScanPlan Plan { get; set; }

    public int RulesCount { get; }

    public IReadOnlyDictionary<string, string> CategoryByRule { get; }

    public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        IProgress<string>? perehodnik = progress is null
            ? null
            : new Progress<string>(put => progress.Report(new ScanProgress(put, 0, 0)));

        return PolnyyProhod.ScanAsync(Plan, perehodnik, ct);
    }
}

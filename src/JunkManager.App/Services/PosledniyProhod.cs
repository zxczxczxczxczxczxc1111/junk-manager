using JunkManager.Core;

namespace JunkManager.App.Services;

/// <summary>
/// The result of the last scan, shared by every screen that shows it.
/// </summary>
/// <remarks>
/// <para>
/// Раскладка задачи 9 давала экрану «Файлы» свою команду сканирования. Так
/// делать нельзя: проход по рабочей машине идёт двадцать пять секунд, и второй
/// проход по тому же диску это не только вторые двадцать пять секунд, но и
/// второй набор чисел. «Найдено 4,7 ГБ» на обзоре и другое число на очистке, и
/// никакого способа понять, какое из них правда.
/// </para>
/// <para>
/// Поэтому проход один, и его итог живёт здесь. Обзор кладёт, «Файлы» читает.
/// Кнопка «Сканировать заново» есть только на обзоре, и это тоже решение: два
/// места, откуда запускается одна и та же долгая работа, рано или поздно
/// запускаются одновременно.
/// </para>
/// </remarks>
internal sealed class PosledniyProhod
{
    /// <summary>Итог последнего прохода, либо null, пока прохода не было.</summary>
    public ScanResult? Itog { get; private set; }

    /// <summary>
    /// Появился новый итог. Не PropertyChanged: подписчику важно СОБЫТИЕ смены,
    /// а не имя свойства, и путать эти две вещи значит однажды перерисовать
    /// экран на изменении чего-то другого.
    /// </summary>
    public event EventHandler<ScanResult>? Obnovilsya;

    public void Polozhit(ScanResult itog)
    {
        ArgumentNullException.ThrowIfNull(itog);

        Itog = itog;
        Obnovilsya?.Invoke(this, itog);
    }
}

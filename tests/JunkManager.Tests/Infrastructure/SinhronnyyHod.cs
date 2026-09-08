namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Приёмник хода, который выполняет обработчик СРАЗУ и в том же потоке.
/// </summary>
/// <remarks>
/// <para>
/// Стандартный <see cref="Progress{T}"/> сюда не годится, и это выяснилось
/// падением при полном прогоне 05.09.2026, хотя поодиночке класс был зелёным.
/// Без контекста синхронизации <c>Progress</c> кидает каждый отчёт в пул
/// потоков: порядок не гарантирован, доставка не дожидается, и проверка
/// «последний отчёт говорит 4 из 4» сверяет то, что успело прийти.
/// </para>
/// <para>
/// Отдельно это ломало проверку остановки: она отменяет прогон ИЗ отчёта о
/// ходе, а отложенный отчёт приходит, когда останавливать уже нечего.
/// </para>
/// <para>
/// В окне такой подмены не нужно: там контекст это диспетчер, и отчёты
/// приходят по порядку. Приспособление воспроизводит именно это поведение, а
/// не обходит его.
/// </para>
/// </remarks>
public sealed class SinhronnyyHod<T>(Action<T> obrabotchik) : IProgress<T>
{
    private readonly Action<T> _obrabotchik =
        obrabotchik ?? throw new ArgumentNullException(nameof(obrabotchik));

    public void Report(T value) => _obrabotchik(value);
}

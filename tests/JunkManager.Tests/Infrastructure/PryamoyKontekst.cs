using System.Threading;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Контекст синхронизации, который выполняет присланное СРАЗУ и на месте.
/// </summary>
/// <remarks>
/// <para>
/// Нужен там, где проверяется <see cref="Progress{T}"/>. Тот захватывает
/// текущий контекст при создании и шлёт отчёты в него. В окне контекст это
/// диспетчер, и отчёты приходят по порядку и до продолжения после await. В
/// проверке контекста нет вовсе, поэтому <c>Progress</c> кидает каждый отчёт в
/// пул потоков, и к моменту сверки половина ещё не выполнилась.
/// </para>
/// <para>
/// Без него проверка хода очистки падает через раз и выглядит как плавающий
/// дефект продукта, которого нет. Ждать в цикле нельзя: ожидание с запасом
/// прячет настоящую потерю отчётов, ради которой проверка и написана.
/// </para>
/// </remarks>
public sealed class PryamoyKontekst : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        d(state);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        d(state);
    }
}

using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// One STA thread with a live dispatcher, plus the application resources, for
/// tests that build real WPF objects.
/// </summary>
/// <remarks>
/// <para>
/// Поток ОДИН на весь набор, и это не оптимизация. Первая попытка поднимала
/// свой поток на каждый тест, и второй падал с «The calling thread cannot
/// access this object because a different thread owns it»: <c>Application</c> в
/// домене существует ровно один, создаётся в первом же потоке и навсегда
/// принадлежит ему.
/// </para>
/// <para>
/// <c>Application.Shutdown</c> здесь не вызывается никогда. Он закрывает
/// приложение на весь домен, и следующий тест получил бы мёртвые ресурсы.
/// </para>
/// </remarks>
public sealed class StaStend : IDisposable
{
    private readonly Dispatcher _dispatcher;

    public StaStend()
    {
        using var gotov = new ManualResetEventSlim(false);
        Dispatcher? poymannyy = null;

        var potok = new Thread(() =>
        {
            poymannyy = Dispatcher.CurrentDispatcher;
            gotov.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "sta-stend",
        };

        potok.SetApartmentState(ApartmentState.STA);
        potok.Start();

        if (!gotov.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new InvalidOperationException("поток STA не поднялся за 30 секунд");
        }

        _dispatcher = poymannyy!;

        Vypolnit(() =>
        {
            var app = Application.Current ?? new JunkManager.App.App();

            if (app.Resources.MergedDictionaries.Count == 0)
            {
                // InitializeComponent у App вызывает его собственный Main,
                // которого в наборе тестов нет. Грузим App.xaml сами, целиком:
                // собирать словари по одному значило бы задать свой порядок
                // подключения, а половина дефектов этого класса это как раз
                // порядок.
                Application.LoadComponent(
                    app, new Uri("/JunkManager;component/App.xaml", UriKind.Relative));
            }
        });
    }

    /// <summary>
    /// Выполняет действие в потоке STA. Исключение перебрасывается вызывающему:
    /// без этого поток тихо умирал бы, а тест оставался зелёным.
    /// </summary>
    public void Vypolnit(Action deystvie) => _dispatcher.Invoke(deystvie);

    /// <summary>
    /// То же для действия с ожиданием внутри.
    /// </summary>
    /// <remarks>
    /// <c>Invoke</c> здесь не годится: он ЗАБЛОКИРУЕТ поток диспетчера до
    /// конца задачи, а задача ждёт отчётов о ходе, которые идут через тот же
    /// диспетчер. Получилась бы взаимная блокировка, неотличимая от зависшего
    /// окна. <c>InvokeAsync</c> отдаёт задачу наружу, диспетчер продолжает
    /// разбирать очередь, и ждёт только вызывающий поток.
    /// </remarks>
    public Task VypolnitAsync(Func<Task> deystvie) =>
        _dispatcher.InvokeAsync(deystvie).Task.Unwrap();

    public void Dispose() => _dispatcher.InvokeShutdown();
}

/// <summary>
/// Набор, внутри которого тесты не идут параллельно: <c>Application</c> один на
/// домен, и два теста, строящих окна одновременно, дерутся за него.
/// </summary>
[CollectionDefinition(Imya, DisableParallelization = true)]
public sealed class StaNabor : ICollectionFixture<StaStend>
{
    public const string Imya = "wpf-sta";
}

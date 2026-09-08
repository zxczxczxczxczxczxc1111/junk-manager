using System.Diagnostics;
using System.Text;
using System.Windows.Diagnostics;

namespace JunkManager.Tests.Infrastructure;

/// <summary>
/// Ловит ошибки привязок WPF, пока живёт.
/// </summary>
/// <remarks>
/// <para>
/// Привязка к несуществующему свойству НЕ ломает сборку и НЕ бросает. Она молча
/// показывает пустоту, и узнаётся это у человека. Это главный класс дефектов
/// продукта: механизм собран, зарегистрирован, выглядит рабочим и никем не
/// зовётся. Единственное место, где WPF о таком сообщает, это трассировка.
/// </para>
/// <para>
/// <c>PresentationTraceSources.Refresh</c> обязателен: без него источник
/// трассировки существует, но не пишет ничего, и ловушка молча ловит ноль.
/// </para>
/// </remarks>
public sealed class LovushkaPrivyazok : IDisposable
{
    private readonly Sobiratel _sobiratel = new();
    private readonly SourceLevels _prezhniy;

    public LovushkaPrivyazok()
    {
        PresentationTraceSources.Refresh();

        _prezhniy = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_sobiratel);
    }

    public IReadOnlyList<string> Oshibki => _sobiratel.Stroki;

    public void Dispose()
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(_sobiratel);
        PresentationTraceSources.DataBindingSource.Switch.Level = _prezhniy;
        _sobiratel.Dispose();
    }

    /// <summary>
    /// Слушатель, который копит строки. Трассировка приходит кусками: сначала
    /// Write без перевода строки, потом WriteLine, и склеивать их надо самому,
    /// иначе одна ошибка приезжает тремя обрывками.
    /// </summary>
    private sealed class Sobiratel : TraceListener
    {
        private readonly StringBuilder _tekushchaya = new();
        private readonly List<string> _stroki = [];

        public IReadOnlyList<string> Stroki => _stroki;

        public override void Write(string? message) => _tekushchaya.Append(message);

        public override void WriteLine(string? message)
        {
            _tekushchaya.Append(message);
            var stroka = _tekushchaya.ToString().Trim();
            _tekushchaya.Clear();

            if (stroka.Length > 0)
            {
                _stroki.Add(stroka);
            }
        }
    }
}

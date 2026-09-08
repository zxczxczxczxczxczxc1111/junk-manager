using CommunityToolkit.Mvvm.ComponentModel;
using JunkManager.Core.Registry;
using JunkManager.Safety;
using Microsoft.Win32;

namespace JunkManager.App.ViewModels;

/// <summary>
/// Одна битая запись реестра как строка списка.
/// </summary>
/// <remarks>
/// Держит находку ядра целиком в <see cref="Source"/>, а наружу отдаёт готовые
/// к показу строки. Разбирать находку в разметке нельзя: вид реестра и признак
/// «значение или ключ» решают, ЧТО именно удалится, и превращать их в текст надо
/// один раз и в одном месте.
/// </remarks>
internal sealed class RegistryFindingViewModel : ObservableObject
{
    private bool _isSelected;

    public RegistryFindingViewModel(RegistryFinding source, bool elevated)
    {
        ArgumentNullException.ThrowIfNull(source);

        Source = source;

        // Ветка машины без прав администратора не удалится, и отметка означала
        // бы кнопку, которая обещает и не может. Решение 3 в шапке плана: саму
        // находку при этом показываем, потому что нашли её честно.
        CanSelect = elevated || source.Hive != RegistryHive.LocalMachine;
    }

    public RegistryFinding Source { get; }

    /// <summary>Имя ветки из файла правил: «Автозапуск текущего пользователя».</summary>
    public string Category => Source.Name;

    /// <summary>Адрес ветки без имени значения. Именно его читает reg.exe.</summary>
    public string KeyPath => RegistryAddress.Format(Source.Hive, Source.SubKey, null);

    /// <summary>Пусто у находки-ключа: у ключа удаляется он сам, а не значение.</summary>
    public string ValueName => Source.ValueName;

    public string MissingTarget => Source.MissingTarget;

    public string Consequence => Source.Consequence;

    /// <summary>HKCU или HKLM, как человек набирает в regedit.</summary>
    public string Hive => RegistryAddress.ShortHive(Source.Hive);

    /// <summary>
    /// Что именно уйдёт. Решение 7 в шапке плана: в макете здесь стояла кнопка
    /// с одинаковой подписью у всех строк, а разница «строка» против «ключ
    /// целиком» это разница, ради которой человек и читает список.
    /// </summary>
    public string KindLabel => Source.Kind switch
    {
        RegistryEntryKind.Value => "значение",
        RegistryEntryKind.Key => "ключ целиком",
        _ => throw new InvalidOperationException($"неизвестный вид записи {Source.Kind}"),
    };

    /// <summary>
    /// Пометка проекции. Пусто у Default: 32- и 64-битный виды дают две строки
    /// с одинаковым путём, и без пометки человек читает это как дубль.
    /// </summary>
    public string ViewLabel => Source.View == RegistryView.Default
        ? string.Empty
        : Source.View.ToString();

    /// <summary>Можно ли вообще отметить эту строку.</summary>
    public bool CanSelect { get; }

    /// <summary>
    /// Отмечена ли строка. Ничего не отмечено заранее, и это не осторожность:
    /// запись реестра не возвращается ни в какую корзину, а число отмеченного
    /// человек читает один раз и глазами.
    /// </summary>
    /// <remarks>
    /// Написано руками, а не через <c>[ObservableProperty]</c>: у строки без
    /// прав отметка обязана НЕ ставиться, а генератор такого запрета не умеет.
    /// Исключение здесь тоже не годится: сюда приходит привязка из разметки, а
    /// исключение в привязке WPF либо проглатывается, либо роняет окно.
    /// </remarks>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var mozhno = value && CanSelect;

            // Сообщаем и тогда, когда просьбу отклонили: иначе флажок в
            // разметке остался бы нажатым, а модель считала бы строку
            // неотмеченной, и число отмеченного разошлось бы с картинкой.
            if (_isSelected == mozhno && value == mozhno)
            {
                return;
            }

            _isSelected = mozhno;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Что услышит экранный диктор вместо имени типа. Строки списка это
    /// ContentPresenter без элемента автоматизации, и WPF берёт имя из
    /// ToString самой модели: живой прогон 05.09.2026 показал в дереве
    /// «JunkManager.App.ViewModels.FindingViewModel» на каждой строке.
    /// </summary>
    public override string ToString() =>
        $"{Category}. {KeyPath}. Ссылается на отсутствующий файл {MissingTarget}";
}

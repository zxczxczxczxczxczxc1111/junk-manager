# Пять правок 06.09.2026: замирание окна, живой журнал, адрес обработчика,
# выход из «файл занят» и слово в отчёте.
#
# Все мутации здесь ломают ПОВЕДЕНИЕ и собираются: недостижимого кода и
# неиспользуемых полей не заводится, см. оговорку в шапке mutate.ps1.
#
# Мутации на сам Restart Manager тут отсутствуют намеренно. Проверено фактом на
# этой машине: система закрывает попрошенного держателя, поэтому договор
# «успех только по освобождённому файлу» на ней не нарушается ничем, что можно
# записать одной подстановкой. Живая проверка его всё равно стережёт, и она
# была КРАСНОЙ дважды за день, когда договор ломался по-настоящему.

@(
    @{ Imya = 'Перебор очистки возвращается на поток окна'
       Fayl = 'src/JunkManager.Deletion/CleanupRunner.cs'
       Iz   = 'return Task.Run('
       V    = 'return ((Func<Func<Task<CleanupReport>>, CancellationToken, Task<CleanupReport>>)((rabota, _) => rabota()))(' }

    @{ Imya = 'Адрес обработчика снова уходит в файловый удалитель'
       Fayl = 'src/JunkManager.Deletion/CleanupRunner.cs'
       Iz   = 'if (!FindingPath.IsFileSystem(nahodka.Path))'
       V    = 'if (false && !FindingPath.IsFileSystem(nahodka.Path))' }

    @{ Imya = 'Переход в раздел перестаёт сообщать экрану о показе'
       Fayl = 'src/JunkManager.App/ViewModels/ShellViewModel.cs'
       Iz   = 'PoslednyyPokaz = Current.Screen?.PriPokazeAsync(CancellationToken.None)'
       V    = 'PoslednyyPokaz = (Current.Screen is null ? null : Task.CompletedTask)' }

    @{ Imya = 'Журнал на показе больше не перечитывается'
       Fayl = 'src/JunkManager.App/ViewModels/HistoryViewModel.cs'
       Iz   = 'public Task PriPokazeAsync(CancellationToken ct) => ZagruzitAsync(ct);'
       V    = 'public Task PriPokazeAsync(CancellationToken ct) => Task.Delay(0, ct);' }

    @{ Imya = 'Обзор на показе больше не перечитывает занятость тома'
       Fayl = 'src/JunkManager.App/ViewModels/OverviewViewModel.cs'
       Iz   = '        ObnovitTom();
        return Task.CompletedTask;'
       V    = '        return Task.CompletedTask;' }

    @{ Imya = 'Занятый файл снова становится пропуском, а не неудачей'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'put, DeleteStatus.Failed, 0, "файл занят: " + ex.Message, Derzhatel(put));'
       V    = 'put, DeleteStatus.Skipped, 0, "файл занят: " + ex.Message, Derzhatel(put));' }

    # Образец переписан 06.09.2026: отбор переехал с `if` на тернарный
    # оператор, и старая мутация молча перестала что-либо проверять. Прогон
    # это поймал и назвал устаревшей, ради чего проверка на образец и стоит.
    @{ Imya = 'В список с кнопками отбираются все неудачи, а не строки с держателем'
       Fayl = 'src/JunkManager.App/ViewModels/FilesViewModel.cs'
       Iz   = 'var nahodka = string.IsNullOrWhiteSpace(ishod.HoldingProcess)'
       V    = 'var nahodka = (ishod.Status == DeleteStatus.Deleted)' }

    @{ Imya = 'Неуслышанная просьба закрыться выдаётся за успех'
       Fayl = 'src/JunkManager.App/ViewModels/ZanyatyyViewModel.cs'
       Iz   = 'if (!itog.Ok)'
       V    = 'if (false && !itog.Ok)' }

    @{ Imya = 'Откладывание на загрузку тут же пробует удалить файл'
       Fayl = 'src/JunkManager.App/ViewModels/ZanyatyyViewModel.cs'
       Iz   = 'if (!povtorit)'
       V    = 'if (povtorit && !povtorit)' }

    @{ Imya = 'Освобождённый, но не удалённый файл объявляется решённым'
       Fayl = 'src/JunkManager.App/ViewModels/ZanyatyyViewModel.cs'
       Iz   = 'if (udalenie.Status == DeleteStatus.Deleted)'
       V    = 'if (udalenie.Status != DeleteStatus.Cancelled)' }

    @{ Imya = 'Блок занятых файлов привязан не к своему списку'
       Fayl = 'src/JunkManager.App/Views/FilesView.xaml'
       Iz   = 'ItemsSource="{Binding Stuck}"'
       V    = 'ItemsSource="{Binding Report.Outcomes}"' }

    @{ Imya = 'Кнопка откладывания зовёт принудительное завершение'
       Fayl = 'src/JunkManager.App/Views/FilesView.xaml'
       Iz   = 'Command="{Binding OtlozhitCommand}"'
       V    = 'Command="{Binding ZavershitCommand}"' }

    @{ Imya = 'Ход просьбы перестаёт быть виден на экране'
       Fayl = 'src/JunkManager.App/Views/FilesView.xaml'
       Iz   = 'AutomationProperties.AutomationId="stuck-progress"'
       V    = 'AutomationProperties.AutomationId="stuck-hod"' }

    # Ниже мутации под переделку от 06.09.2026: продукт просит держателя сам,
    # два оставшихся выхода показываются только после отказа, а занятые строки
    # не повторяются в общем списке итога.

    @{ Imya = 'Автоматическая просьба уходит с уже отменённым признаком'
       Fayl = 'src/JunkManager.App/ViewModels/FilesViewModel.cs'
       Iz   = 'await PoprositDerzhateleyAsync(Report, _otmenaOchistki.Token).ConfigureAwait(true);'
       V    = 'await PoprositDerzhateleyAsync(Report, new CancellationToken(true)).ConfigureAwait(true);' }

    @{ Imya = 'Прерванная очистка всё равно закрывает чужие программы'
       Fayl = 'src/JunkManager.App/ViewModels/FilesViewModel.cs'
       Iz   = 'if (otchet.Cancelled)'
       V    = 'if (false && otchet.Cancelled)' }

    @{ Imya = 'Занятая строка дублируется в общем списке итога'
       Fayl = 'src/JunkManager.App/ViewModels/FilesViewModel.cs'
       Iz   = '            var stroka = new ZanyatyyViewModel(nahodka, ishod, _zanyatye, PovtoritAsync);'
       V    = "            ReportRows.Add(ishod);`r`n            var stroka = new ZanyatyyViewModel(nahodka, ishod, _zanyatye, PovtoritAsync);" }

    @{ Imya = 'Два выхода показываются, не дождавшись просьбы'
       Fayl = 'src/JunkManager.App/ViewModels/ZanyatyyViewModel.cs'
       Iz   = 'public bool DeystviyaVidny => ProsbaProshla && !Resheno;'
       V    = 'public bool DeystviyaVidny => !Resheno;' }

    @{ Imya = 'Неудавшаяся просьба оставляет строку без единого выхода'
       Fayl = 'src/JunkManager.App/ViewModels/ZanyatyyViewModel.cs'
       Iz   = '            ProsbaProshla = true;'
       V    = '            ProsbaProshla = Resheno;' }
)

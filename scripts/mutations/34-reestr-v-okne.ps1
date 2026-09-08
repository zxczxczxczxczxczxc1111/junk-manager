# Реестр в окне
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.
#
# Номер 34 занят ЭТИМ файлом. План программ бронировал тот же номер под
# 34-granica-ochistki.ps1, и ему придётся взять другой: коллизия разобрана
# в отчёте задачи 6.

@(
    @{ Imya = 'Удаляется весь список, а не отмеченное'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'var vybrannye = Rows.Where(r => r.IsSelected).Select(r => r.Source).ToList();'
       V    = 'var vybrannye = Rows.Select(r => r.Source).ToList();' }

    @{ Imya = 'Ворота согласия внутри команды удаления снимаются'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'if (!Confirmed || Step != FlowStep.Confirm)'
       V    = 'if (false && !Confirmed)' }

    # Образец берёт и пояснение над строками: пара «Confirmed = false» плюс
    # «Step = FlowStep.Selection» встречается ещё и в Zavershit, а мутатор на
    # неоднозначном образце падает намеренно.
    @{ Imya = 'Возврат к выбору сохраняет прошлое согласие'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = @'
        // другой список.
        Confirmed = false;
        Step = FlowStep.Selection;
'@
       V    = @'
        // другой список.
        Step = FlowStep.Selection;
'@ }

    # НЕ 'foreach (var stroka in Rows)': та мутация ВЫЖИЛА при прогоне
    # 06.09.2026, и это не дыра в проверках. Отметку на ветке машины отбивает
    # вторая преграда, сама строка (`var mozhno = value && CanSelect`), поэтому
    # снятие фильтра здесь не меняет ничего наблюдаемого. Мутация, ничего не
    # меняющая, ничего и не проверяет. Обе преграды намеренно избыточны, и у
    # каждой своя проверка: у строки это
    # Otmetka_na_stroke_bez_prav_ne_stavitsya_dazhe_esli_poprosit, у кнопки
    # мутация ниже.
    @{ Imya = 'Отметить всё отмечает ровно то, что отметить нельзя'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'foreach (var stroka in Rows.Where(r => r.CanSelect))'
       V    = 'foreach (var stroka in Rows.Where(r => !r.CanSelect))' }

    @{ Imya = 'Пропуски сканирования перестают попадать в плашку'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'if (itog.Skipped.Count > 0)'
       V    = 'if (false && itog.Skipped.Count > 0)'
       Vse  = $true }

    @{ Imya = 'К подтверждению переходим и без единой отметки'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'if (!CanGoToConfirm)'
       V    = 'if (false && !CanGoToConfirm)' }

    # Не «страховку не зовут вовсе»: без await метод перестаёт быть async, это
    # CS1998, а при TreatWarningsAsErrors провал сборки. Ломается доставка
    # заметки на экран, вызов при этом остаётся.
    @{ Imya = 'Заметка о страховке не доезжает до подтверждения'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'RestorePointNote = await _ochistka.ZastrahovatAsync(ct).ConfigureAwait(true);'
       V    = '_ = await _ochistka.ZastrahovatAsync(ct).ConfigureAwait(true);' }

    @{ Imya = 'Новая проверка оставляет поток на подтверждении'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = @'
        Step = FlowStep.Selection;
        Confirmed = false;
        Pereschitat();
'@
       V    = @'
        Confirmed = false;
        Pereschitat();
'@ }

    @{ Imya = 'Пересчёт дописывает в список подтверждения, не очистив его'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'SelectedRows.Clear();'
       V    = '' }

    @{ Imya = 'Строка ветки машины отмечается без прав администратора'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryFindingViewModel.cs'
       Iz   = 'var mozhno = value && CanSelect;'
       V    = 'var mozhno = value;' }

    @{ Imya = 'Точка восстановления перестаёт делаться вовсе'
       Fayl = 'src/JunkManager.App/Services/RegistryCleanupService.cs'
       Iz   = 'if (_tochkuUzheProbovali)'
       V    = 'if (true || _tochkuUzheProbovali)' }
    # ==== Откат из бэкапа, задача 7 ====

    @{ Imya = 'Импорт перестаёт проверять, что файл из своего каталога'
       Fayl = 'src/JunkManager.App/Services/RegistryBackupsService.cs'
       Iz   = 'if (!Svoy(file.FilePath))'
       V    = 'if (false && !Svoy(file.FilePath))' }

    @{ Imya = 'Негодный бэкап показывается годным'
       Fayl = 'src/JunkManager.App/Services/RegistryBackupsService.cs'
       Iz   = 'var godnyy = RegistryBackup.Proverit(fayl, out var prichina);'
       V    = 'RegistryBackup.Proverit(fayl, out var prichina); var godnyy = true;' }

    @{ Imya = 'Список бэкапов перестаёт быть отсортированным по свежести'
       Fayl = 'src/JunkManager.App/Services/RegistryBackupsService.cs'
       Iz   = 'itog.Sort((a, b) => b.WrittenUtc.CompareTo(a.WrittenUtc));'
       V    = 'itog.Sort((a, b) => a.WrittenUtc.CompareTo(b.WrittenUtc));' }

    # Сравнение строк вместо канонизированного пути. Путь вида
    # <каталог>\..\chuzhoy.reg НАЧИНАЕТСЯ с каталога бэкапов и заканчивается
    # где угодно, поэтому такая проверка пропускает выход наружу.
    @{ Imya = 'Свой каталог сверяется строкой, а не канонизированным путём'
       Fayl = 'src/JunkManager.App/Services/RegistryBackupsService.cs'
       Iz   = 'var polnyy = Path.GetFullPath(put);'
       V    = 'var polnyy = put;' }

    @{ Imya = 'Негодный файл доезжает до импорта из окна'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'if (fayl is null || !fayl.Valid)'
       V    = 'if (fayl is null)' }

    @{ Imya = 'Неудачный откат чистит список так же, как удачный'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'if (!itog.Ok)
        {
            return;
        }'
       V    = 'if (false && !itog.Ok)
        {
            return;
        }' }

    @{ Imya = 'Удачный откат оставляет устаревший список находок'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = '        Rows.Clear();
        Confirmed = false;
        Step = FlowStep.Selection;
        Pereschitat();

        State.Pusto(
            "Ветка восстановлена",'
       V    = '        Confirmed = false;
        Step = FlowStep.Selection;
        Pereschitat();

        State.Pusto(
            "Ветка восстановлена",' }

    @{ Imya = 'Закрытая панель бэкапов оставляет заметку прошлого отката'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = '        BackupsOpen = false;
        RollbackNote = null;'
       V    = '        BackupsOpen = false;' }

    @{ Imya = 'Пустой каталог бэкапов не называет своего адреса'
       Fayl = 'src/JunkManager.App/ViewModels/RegistryViewModel.cs'
       Iz   = 'RollbackNote = $"бэкапов пока нет: каталог {_bekapy.Directory} пуст. "'
       V    = 'RollbackNote = $"бэкапов пока нет. "' }
)

# Удаление в реестре
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Удаление в реестре перестаёт требовать удачный бэкап'
       Fayl = 'src/JunkManager.Deletion/RegistryExecutor.cs'
       Iz   = 'if (!bekap.Ok)'
       V    = 'if (false && !bekap.Ok)' }

    @{ Imya = 'Проверка файла бэкапа сводится к коду возврата reg.exe'
       Fayl = 'src/JunkManager.Deletion/RegistryBackup.cs'
       Iz   = 'return Proverit(fayl, out var prichina)'
       V    = 'return true || Proverit(fayl, out var prichina)' }

    @{ Imya = 'Метка UTF-16LE у бэкапа перестаёт проверяться'
       Fayl = 'src/JunkManager.Deletion/RegistryBackup.cs'
       Iz   = 'if (nachalo.Length < 2 || nachalo[0] != 0xFF || nachalo[1] != 0xFE)'
       V    = 'if (false && (nachalo.Length < 2 || nachalo[0] != 0xFF || nachalo[1] != 0xFE))' }

    @{ Imya = 'Заголовок файла бэкапа перестаёт проверяться'
       Fayl = 'src/JunkManager.Deletion/RegistryBackup.cs'
       Iz   = 'if (!tekst.StartsWith(Zagolovok, StringComparison.Ordinal))'
       V    = 'if (false && !tekst.StartsWith(Zagolovok, StringComparison.Ordinal))' }

    @{ Imya = 'Пустой файл бэкапа засчитывается за бэкап'
       Fayl = 'src/JunkManager.Deletion/RegistryBackup.cs'
       Iz   = 'if (dlina == 0)'
       V    = 'if (false && dlina == 0)' }

    @{ Imya = 'В журнал реестра попадают только удачные исходы'
       Fayl = 'src/JunkManager.Deletion/RegistryExecutor.cs'
       Vse  = $true
       Iz   = 'await _zhurnal.RecordAsync(itog, CancellationToken.None).ConfigureAwait(false);'
       V    = 'if (itog.Status == DeleteStatus.Deleted) { await _zhurnal.RecordAsync(itog, CancellationToken.None).ConfigureAwait(false); }' }
)

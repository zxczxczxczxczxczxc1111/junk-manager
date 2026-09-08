# Удаление
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Удаление перестаёт перепроверять путь перед самым удалением'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'if (!SafetyGuard.TryVerifyForDeletion(zayavlennyy, out var svezhiy, out var prichina))'
       V    = 'if (!SafetyGuard.TryVerifyForDeletion(zayavlennyy, out var svezhiy, out var prichina) && false)' }

    @{ Imya = 'Удаление каталога заходит внутрь ссылки'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'if (svedeniya.LinkTarget is not null)'
       V    = 'if (false && svedeniya.LinkTarget is not null)' }

    @{ Imya = 'Занятый файл выдаётся за удалённый'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'put, DeleteStatus.Failed, 0, "файл занят: " + ex.Message, Derzhatel(put));'
       V    = 'put, DeleteStatus.Deleted, 0, "файл занят: " + ex.Message, Derzhatel(put));' }

    @{ Imya = 'Флаг «только для чтения» перестаёт сниматься'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'if ((svedeniya.Attributes & FileAttributes.ReadOnly) != 0)'
       V    = 'if (false && (svedeniya.Attributes & FileAttributes.ReadOnly) != 0)' }

    @{ Imya = 'Отмена перестаёт останавливать удаление'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'if (!ct.IsCancellationRequested)'
       V    = 'if (!ct.IsCancellationRequested || DateTime.UtcNow.Year > 0)' }

    @{ Imya = 'В журнал попадают только удачные исходы'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'await _zhurnal.RecordAsync(itog, CancellationToken.None).ConfigureAwait(false);'
       V    = 'if (itog.Status == DeleteStatus.Deleted) { await _zhurnal.RecordAsync(itog, CancellationToken.None).ConfigureAwait(false); }' }
)

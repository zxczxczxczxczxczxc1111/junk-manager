# Журнал
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Журнал копит записи в буфере вместо сброса на диск'
       Fayl = 'src/JunkManager.Deletion/JsonlOperationLog.cs'
       Iz   = '{ AutoFlush = true }'
       V    = '{ AutoFlush = false }' }

    @{ Imya = 'Живой журнал открывается без разделения с пишущим'
       Fayl = 'src/JunkManager.Deletion/JsonlOperationLog.cs'
       Iz   = 'filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);'
       V    = 'filePath, FileMode.Open, FileAccess.Read, FileShare.Read);' }

    @{ Imya = 'Кириллица в журнале экранируется'
       Fayl = 'src/JunkManager.Deletion/JsonlOperationLog.cs'
       Iz   = 'Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,'
       V    = '' }
)

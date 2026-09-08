# Откат
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Импорт перестаёт проверять файл перед импортом'
       Fayl = 'src/JunkManager.Deletion/RegistryRollback.cs'
       Iz   = 'if (!RegistryBackup.Proverit(backupFile, out var prichina))'
       V    = 'if (!RegistryBackup.Proverit(backupFile, out var prichina) && false)' }

    @{ Imya = 'Возврат значения проверяется наличием, а не содержимым'
       Fayl = 'src/JunkManager.Deletion/RegistryRollback.cs'
       Iz   = 'return string.Equals(teper, expected, StringComparison.Ordinal);'
       V    = 'return teper is not null;' }
)

# Платформенные утилиты
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Отбор драйверов перестаёт смотреть на привязанные устройства'
       Fayl = 'src/JunkManager.Core/Sources/Platform/PnpDriverStore.cs'
       Iz   = '[.. all.Where(p => !p.HasDevices && activeNames.Contains(p.OriginalName))]'
       V    = '[.. all]' }

    @{ Imya = 'В команду DISM возвращается ResetBase'
       Fayl = 'src/JunkManager.Core/Sources/Platform/DismComponentStore.cs'
       Iz   = '["/Online", "/English", "/Cleanup-Image", "/StartComponentCleanup"]'
       V    = '["/Online", "/English", "/Cleanup-Image", "/StartComponentCleanup", "/ResetBase"]' }

    @{ Imya = 'Разбор DISM перестаёт быть fail-closed'
       Fayl = 'src/JunkManager.Core/Sources/Platform/DismComponentStore.cs'
       # В плане тут стояло 'if (backups is null)' -> 'if (false)'. Не годится
       # дважды: недостижимый throw это CS0162, а без него проверка на null
       # пропадает и ParseSize(backups) валится на CS8604. Мутация не собралась
       # бы, то есть не проверила бы ничего. Тот же дефект вносится честнее у
       # источника: разбор перестаёт отличать «метки нет» от «там ноль».
       Iz   = 'return null;'
       V    = 'return "0 bytes";' }
)

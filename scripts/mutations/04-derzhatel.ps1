# Определитель держателя
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Сессия Restart Manager не закрывается'
       Fayl = 'src/JunkManager.Deletion/LockedFileInspector.cs'
       Iz   = 'kodZakrytiya = Native.RmEndSession(sessiya);'
       # Не константа НАМЕРЕННО: при 'kodZakrytiya = 0' проверка ниже
       # схлопывается в константную ложь, тело становится недостижимым, и
       # мутация падает на CS0162, ничего не проверив.
       V    = 'kodZakrytiya = (int)(sessiya * 0);' }

    @{ Imya = 'Свободный файл выдаётся за невозможность спросить'
       Fayl = 'src/JunkManager.Deletion/LockedFileInspector.cs'
       Iz   = 'if (kod == Native.ErrorSuccess && nuzhno == 0)'
       V    = 'if (false && kod == Native.ErrorSuccess && nuzhno == 0)' }

    @{ Imya = 'Отказ по занятости перестаёт называть держателя'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'if (LockedFileInspector.TryGetHolders(put, out var kto, out _) && kto.Count > 0)'
       V    = 'if (LockedFileInspector.TryGetHolders(put, out var kto, out _) && kto.Count > 0 && false)' }
)

# Guard реестра
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Guard реестра перестаёт спрашивать список разрешённых веток'
       Fayl = 'src/JunkManager.Safety/RegistryGuard.cs'
       Iz   = 'if (hive == allowedHive && IsAtOrUnder(chistaya, allowedSubKey))'
       V    = 'if (hive == allowedHive || IsAtOrUnder(chistaya, allowedSubKey) || true)' }

    @{ Imya = 'Безусловный запрет на SYSTEM, SECURITY и SAM снимается'
       Fayl = 'src/JunkManager.Safety/RegistryGuard.cs'
       Iz   = 'if (hive == deniedHive && IsAtOrUnder(chistaya, deniedSubKey))'
       V    = 'if (false && hive == deniedHive && IsAtOrUnder(chistaya, deniedSubKey))' }

    @{ Imya = 'Значение по умолчанию становится удаляемым'
       Fayl = 'src/JunkManager.Safety/RegistryGuard.cs'
       Iz   = 'if (string.IsNullOrEmpty(valueName))'
       V    = 'if (false && string.IsNullOrEmpty(valueName))' }

    @{ Imya = 'Вложенность в реестре подменяется префиксом строки'
       Fayl = 'src/JunkManager.Safety/RegistryGuard.cs'
       Iz   = '|| c.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);'
       V    = '|| c.StartsWith(r, StringComparison.OrdinalIgnoreCase);' }

    @{ Imya = 'Разрешённый корень целиком становится удаляемым ключом'
       Fayl = 'src/JunkManager.Safety/RegistryGuard.cs'
       Iz   = '&& chistaya.Equals(Normalize(allowedSubKey), StringComparison.OrdinalIgnoreCase))'
       V    = '&& false && chistaya.Equals(Normalize(allowedSubKey), StringComparison.OrdinalIgnoreCase))' }
)

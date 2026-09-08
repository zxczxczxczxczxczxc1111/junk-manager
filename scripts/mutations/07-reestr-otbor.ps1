# Отбор находок реестра
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Неразобранное значение становится находкой'
       Fayl = 'src/JunkManager.Core/Registry/RegistryScanner.cs'
       Iz   = 'if (!RegistryTargetExtractor.TryExtract(syroe, out var cel, out var otkaz))'
       V    = 'if (!RegistryTargetExtractor.TryExtract(syroe, out var cel, out var otkaz) && false)' }

    @{ Imya = 'Неизвестное состояние цели засчитывается за отсутствие файла'
       Fayl = 'src/JunkManager.Core/Registry/RegistryScanner.cs'
       Iz   = 'case TargetState.Unknown:'
       V    = 'case (TargetState)99:' }

    @{ Imya = 'Значение по умолчанию попадает в обход'
       Fayl = 'src/JunkManager.Core/Registry/RegistryScanner.cs'
       Iz   = 'if (string.IsNullOrEmpty(imya))'
       V    = 'if (false && string.IsNullOrEmpty(imya))' }

    @{ Imya = 'Отказ по отсутствующему тому снимается'
       Fayl = 'src/JunkManager.Core/Registry/RegistryTargetExtractor.cs'
       Iz   = 'if (koren.Length == 0 || !Directory.Exists(koren))'
       V    = 'if (false && (koren.Length == 0 || !Directory.Exists(koren)))' }

    @{ Imya = 'Первое слово берётся без проверки на исполняемый файл'
       Fayl = 'src/JunkManager.Core/Registry/RegistryTargetExtractor.cs'
       Iz   = 'if (Ispolnyaemyy(pervoeSlovo))'
       V    = 'if (Ispolnyaemyy(pervoeSlovo) || pervoeSlovo.Length > 0)' }
)

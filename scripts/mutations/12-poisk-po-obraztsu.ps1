# Поиск по образцу
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Поиск по образцу заходит внутрь junction'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = 'if (new DirectoryInfo(sub).LinkTarget is not null)'
       V    = 'if (false && new DirectoryInfo(sub).LinkTarget is not null)' }

    @{ Imya = 'Корень поиска по образцу перестаёт спрашивать guard'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = 'if (!SafetyGuard.TryVerify(root, out var verified, out var reason))'
       V    = 'if (!SafetyGuard.TryVerify(root, out var verified, out var reason) && false)' }

    @{ Imya = 'Файл в поиске по образцу перестаёт спрашивать guard'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = 'if (!SafetyGuard.TryVerify(file, out var verified, out var reason))'
       V    = 'if (!SafetyGuard.TryVerify(file, out var verified, out var reason) && false)' }

    @{ Imya = 'Отсечка по возрасту в поиске по образцу снимается'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = 'if (info.LastWriteTimeUtc > cutoffUtc)'
       V    = 'if (false && info.LastWriteTimeUtc > cutoffUtc)' }

    @{ Imya = 'Ограничение глубины поиска по образцу снимается'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = 'if (depth > options.MaxDepth || ct.IsCancellationRequested)'
       V    = 'if (depth > int.MaxValue || ct.IsCancellationRequested)' }

    # Vse намеренно: ступень Risk ставится в двух местах, у находки-файла и у
    # находки-каталога. Класс дефекта один на оба, и правка только первого
    # проверяла бы половину. С 05.09.2026 неоднозначный образец без Vse роняет
    # прогон, и это правильно: он молча правил бы одно из двух.
    @{ Imya = 'Находка по образцу выдаётся за безопасную'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Vse  = $true
       Iz   = 'Tier: RiskTier.Risk,'
       V    = 'Tier: RiskTier.Safe,' }
)

# Поиск по образцу: маски каталогов
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'obrazcy: имя каталога перестаёт проверяться при обходе'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = 'if (Nazvannyy(sub, options, cutoffUtc, findings, skipped))'
       V    = 'if (false && Nazvannyy(sub, options, cutoffUtc, findings, skipped))' }

    @{ Imya = 'obrazcy: названный каталог перестаёт спрашивать про возраст'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = 'if (DirectorySize.NewestWriteUtc(directory) > cutoffUtc)'
       V    = 'if (false && DirectorySize.NewestWriteUtc(directory) > cutoffUtc)' }

    @{ Imya = 'obrazcy: маски каталогов сравниваются с учётом регистра'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = 'if (!options.DirectoryMasks.Contains(name, StringComparer.OrdinalIgnoreCase))'
       V    = 'if (!options.DirectoryMasks.Contains(name, StringComparer.Ordinal))' }

    @{ Imya = 'obrazcy: после названного каталога обход всё равно лезет внутрь'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = @'
                // Named as a whole, so nothing inside is offered separately: a
                // .dmp counted both on its own and inside the folder holding it
                // shows a person twice the space they would actually get back.
                continue;
'@
       V    = @'
                // Named as a whole, so nothing inside is offered separately: a
                // .dmp counted both on its own and inside the folder holding it
                // shows a person twice the space they would actually get back.
                _ = sub;
'@ }

    @{ Imya = 'obrazcy: список масок каталогов пустеет'
       Fayl = 'src/JunkManager.Core/Sources/Pattern/PatternScanner.cs'
       Iz   = '"Crash Reports", "CrashReports", "CrashDumps", "Crashpad", "crashes",'
       V    = '"CrashReports", "Crashpad",' }
)

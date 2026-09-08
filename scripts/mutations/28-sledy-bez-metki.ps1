# Второе плечо поиска следов: каталог без метки внутри
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Каталог без метки снова не рассматривается вовсе'
       Fayl = 'src/JunkManager.Core/Apps/LeftoverFinder.cs'
       Iz   = 'if (!bylaMetka)'
       V    = 'if (false && !bylaMetka)' }

    @{ Imya = 'Второе плечо перестаёт спрашивать про возраст'
       Fayl = 'src/JunkManager.Core/Apps/LeftoverFinder.cs'
       Iz   = @'
        if (pozdneyshiy is null || (seychas - pozdneyshiy.Value).TotalDays < idleDays)
        {
            return;
        }

        var bayt = Razmer(propusk.Value);

        if (bayt < PorogBezMetki)
'@
       V    = @'
        if (pozdneyshiy is null)
        {
            return;
        }

        var bayt = Razmer(propusk.Value);

        if (bayt < PorogBezMetki)
'@ }

    @{ Imya = 'Каталоги самой Windows снова попадают в следы'
       Fayl = 'src/JunkManager.Core/Apps/LeftoverFinder.cs'
       Iz   = @'
                || DetectorExclusions.IsExcluded(imya)
'@
       V    = @'
                || false
'@ }

    @{ Imya = 'VirtualStore пропадает из списка исключений'
       Fayl = 'src/JunkManager.Core/Sources/Detect/DetectorExclusions.cs'
       Iz   = '"VirtualStore", "Package Cache", "Packages", "Microsoft",'
       V    = '"Packages", "Microsoft",' }
)

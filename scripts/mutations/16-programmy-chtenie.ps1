# Чтение установленных программ
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Схлопывание перестаёт различать HKCU и HKLM'
       Fayl = 'src/JunkManager.Core/Apps/InstalledProgramReader.cs'
       Iz   = 'var oblast = zapis.Scope == ProgramScope.User ? "user" : "machine";'
       V    = 'var oblast = "machine";' }

    # Строка Iz взята двумя строками намеренно: одиночная
    # 'zapis.DisplayVersion?.Trim() ?? string.Empty,' встречается в файле дважды,
    # и первое вхождение лежит в Svesti, а не в Klyuch. Без Vse скрипт заменяет
    # ПЕРВОЕ вхождение (проверено чтением mutate.ps1, там IndexOf), то есть
    # мутация ушла бы мимо ключа схлопывания и выжила бы.
    @{ Imya = 'Схлопывание перестаёт смотреть на версию'
       Fayl = 'src/JunkManager.Core/Apps/InstalledProgramReader.cs'
       Iz   = @'
            zapis.DisplayVersion?.Trim() ?? string.Empty,
            zapis.Publisher?.Trim() ?? string.Empty);
'@
       V    = @'
            zapis.Publisher?.Trim() ?? string.Empty);
'@ }

    @{ Imya = 'Дата установки принимает тридцать третий месяц'
       Fayl = 'src/JunkManager.Core/Apps/InstalledProgramReader.cs'
       Iz   = 'if (data.Year < 1995 || data > segodnya)'
       V    = 'if (false && (data.Year < 1995 || data > segodnya))' }

    @{ Imya = 'Размер программы считается сквозь junction'
       Fayl = 'src/JunkManager.Core/Apps/ProgramSizeCalculator.cs'
       Iz   = 'if (new DirectoryInfo(vlozhennyy).LinkTarget is null)'
       V    = 'if (true)' }

    # Образец отличается от написанного в плане: проверка общего корня уехала
    # выше вызова guard и получила свою функцию. Причина записана в отчёте и в
    # самом коде: guard отказывает C:\ и C:\Windows своими словами, а
    # C:\ProgramData пропускает вовсе, то есть проверка обязана быть здесь.
    @{ Imya = 'Общий корень перестаёт быть отказом'
       Fayl = 'src/JunkManager.Core/Apps/ProgramSizeCalculator.cs'
       Iz   = 'if (kanonicheskiy.Equals(kandidat, StringComparison.OrdinalIgnoreCase))'
       V    = 'if (false && kanonicheskiy.Equals(kandidat, StringComparison.OrdinalIgnoreCase))' }

    @{ Imya = 'Неизвестный возраст запуска превращается в ноль'
       Fayl = 'src/JunkManager.Core/Apps/PrefetchUsageReader.cs'
       Iz   = @'
        if (lastRunUtc is null)
        {
            return null;
        }
'@
       V    = @'
        if (lastRunUtc is null)
        {
            return 0;
        }
'@ }

    @{ Imya = 'Подбор файла Prefetch скатывается в сравнение по префиксу'
       Fayl = 'src/JunkManager.Core/Apps/PrefetchUsageReader.cs'
       Iz   = 'if (bezRasshireniya[..defis].Equals(iskomoe, StringComparison.OrdinalIgnoreCase))'
       V    = 'if (bezRasshireniya.StartsWith(iskomoe, StringComparison.OrdinalIgnoreCase))' }
)

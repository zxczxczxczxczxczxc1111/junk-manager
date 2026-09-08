# Удаление программ и поиск следов
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Неразобранная строка удаления всё равно запускается'
       Fayl = 'src/JunkManager.Core/Apps/UninstallCommandBuilder.cs'
       Iz   = @'
        reason = $"строка удаления не разбирается, запуск не производится: '{ochishcheno}'";
        return false;
'@
       V    = @'
        exe = ochishcheno;
        args = [];
        reason = null;
        return true;
'@ }

    @{ Imya = 'MSI собирается с ключом восстановления вместо удаления'
       Fayl = 'src/JunkManager.Core/Apps/UninstallCommandBuilder.cs'
       Iz   = '["/X" + kod, "/qn", "/norestart"], Quiet: true);'
       V    = '["/I" + kod, "/qn", "/norestart"], Quiet: true);' }

    @{ Imya = 'Тихий ключ NSIS теряется, деинсталлятор открывает окно'
       Fayl = 'src/JunkManager.Core/Apps/UninstallCommandBuilder.cs'
       Iz   = 'InstallerKind.Nsis => ["/S"],'
       V    = 'InstallerKind.Nsis => [],' }

    @{ Imya = 'Следы ищутся до успешного удаления'
       Fayl = 'src/JunkManager.Core/Apps/LeftoverFinder.cs'
       Iz   = 'if (result.Outcome != UninstallOutcome.Removed)'
       V    = 'if (false && result.Outcome != UninstallOutcome.Removed)' }

    @{ Imya = 'Стоп-слова перестают вырезаться из токенов'
       Fayl = 'src/JunkManager.Core/Apps/LeftoverFinder.cs'
       Iz   = 'if (kusok.Length < 5 || StopSlova.Contains(kusok))'
       V    = 'if (false && (kusok.Length < 5 || StopSlova.Contains(kusok)))' }

    @{ Imya = 'Совпадение имени каталога скатывается в подстроку'
       Fayl = 'src/JunkManager.Core/Apps/LeftoverFinder.cs'
       Iz   = 'if (chast.Equals(token, StringComparison.OrdinalIgnoreCase))'
       V    = 'if (chast.Contains(token, StringComparison.OrdinalIgnoreCase))' }

    @{ Imya = 'Каталог Crash Reports объявляется меткой мусора'
       Fayl = 'src/JunkManager.Core/Apps/LeftoverFinder.cs'
       Iz   = '"DawnCache", "logs", "Crashpad", "CachedData", "tmp",'
       V    = '"DawnCache", "logs", "Crashpad", "CachedData", "tmp", "Crash Reports",' }

    @{ Imya = 'Деинсталлятор запускается без предохранителя'
       Fayl = 'src/JunkManager.Deletion/UninstallRunner.cs'
       Iz   = 'VmFuse.RequireArmed();'
       V    = '_ = VmFuse.IsArmed;' }

    # Мутации в плане не было: слияние источников появилось по общему
    # ограничению плана («находки РАЗНЫХ источников обязаны проходить ту же
    # дедупликацию по вложенности»), и оно требует своей проверки на двойной
    # счёт. Без неё 5000 байт на диске показывались бы как 10000.
    @{ Imya = 'Слияние источников перестаёт видеть двойной счёт'
       Fayl = 'src/JunkManager.Core/Scanning/SliyanieIstochnikov.cs'
       Iz   = 'if (!Peresekaetsya(kandidat, nahodki, out var pogloshchayushchiy))'
       V    = 'if (!Peresekaetsya(kandidat, nahodki, out var pogloshchayushchiy) || true)' }
)

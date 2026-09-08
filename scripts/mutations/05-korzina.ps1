# Корзина и отложенное удаление
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    #
    # Мутации, которые ловятся ТОЛЬКО классом LiveDestructive, сюда не
    # добавляются намеренно. mutate.ps1 гоняет класс Sandbox, а разрушительный
    # класс на этой машине не запускается вовсе: под непрогретым
    # предохранителем он падает ВСЕГДА, и любая мутация выглядела бы пойманной,
    # не проверив ничего. Разрушительное покрытие доказывается прогоном в
    # госте, вывод записан в плане.

    @{ Imya = 'Корзина перестаёт перепроверять путь перед отправкой'
       Fayl = 'src/JunkManager.Deletion/RecycleBinDeleter.cs'
       Iz   = 'if (!SafetyGuard.TryVerifyForDeletion(zayavlennyy, out var svezhiy, out var prichina))'
       V    = 'if (!SafetyGuard.TryVerifyForDeletion(zayavlennyy, out var svezhiy, out var prichina) && false)' }

    @{ Imya = 'Разбор очереди снова верит, что префикс стоит в начале'
       Fayl = 'src/JunkManager.Deletion/RebootDeleteScheduler.cs'
       Iz   = 'var indeks = istochnik.IndexOf(PrefiksNt, StringComparison.Ordinal);'
       V    = 'var indeks = istochnik.StartsWith(PrefiksNt, StringComparison.Ordinal) ? 0 : -1;' }

    @{ Imya = 'Отложенное удаление перестаёт требовать прав администратора'
       Fayl = 'src/JunkManager.Deletion/RebootDeleteScheduler.cs'
       Iz   = 'if (!elevated)'
       V    = 'if (false && !elevated)' }

    @{ Imya = 'Пустой путь проходит планирование'
       Fayl = 'src/JunkManager.Deletion/RebootDeleteScheduler.cs'
       Iz   = 'if (string.IsNullOrWhiteSpace(put))'
       V    = 'if (false && string.IsNullOrWhiteSpace(put))' }

    @{ Imya = 'Правило с отсечкой снова берёт каталог целиком'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = 'rule.OlderThanDays > 0 || rule.FileFilter is { Length: > 0 };'
       V    = 'false && (rule.OlderThanDays > 0 || rule.FileFilter is { Length: > 0 });' }

    @{ Imya = 'Маска перестаёт переводить в поэлементный режим'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = 'rule.OlderThanDays > 0 || rule.FileFilter is { Length: > 0 };'
       V    = 'rule.OlderThanDays > 0;' }

    @{ Imya = 'Свежий файл всё-таки попадает в цели'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = @'
                        celi?.Add(file);
'@
       V    = @'
                        celi?.Add(file);
                    }

                    if (celi is not null && !Podhodit(info, rule, nowUtc))
                    {
                        celi.Add(file);
'@ }

    @{ Imya = 'Поэлементная находка молча отдаёт пустой список'
       Fayl = 'src/JunkManager.Core/Model/Finding.cs'
       Iz   = '        DeleteScope.SelectedEntries => Targets'
       V    = @'
        DeleteScope.SelectedEntries => Targets ?? [],
        (DeleteScope)999 => Targets
'@ }

    @{ Imya = 'Пустой список целей означает весь каталог'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'if (targets.Count == 0)'
       V    = 'if (false && targets.Count == 0)' }

    @{ Imya = 'Уборка пустых каталогов становится рекурсивной'
       Fayl = 'src/JunkManager.Deletion/FileDeleter.cs'
       Iz   = 'Directory.Delete(katalog, recursive: false);'
       V    = 'Directory.Delete(katalog, recursive: true);' }

    @{ Imya = 'Цель вне корня перестаёт отклоняться'
       Fayl = 'src/JunkManager.Safety/SafetyGuard.cs'
       Iz   = 'if (!IsAtOrUnder(path.Value, root.Value))'
       V    = 'if (false && !IsAtOrUnder(path.Value, root.Value))' }


    @{ Imya = 'Вложенные пути снова считаются дважды'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = 'if (SafetyGuard.Contains(shirokiy.Put, kandidat.Put))'
       V    = 'if (false && SafetyGuard.Contains(shirokiy.Put, kandidat.Put))' }

    @{ Imya = 'Побеждает узкое правило вместо широкого'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = '            .OrderBy(p => p.Kandidat.Put.Value.Length)'
       V    = '            .OrderByDescending(p => p.Kandidat.Put.Value.Length)'
       Vse  = $true }

    @{ Imya = 'Отброшенный путь выбрасывается молча'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = '$"уже посчитан более широким правилом «{pobeditel.Pravilo.Name}» по пути {pobeditel.Put.Value}"'
       V    = '"пропущено"' }
)

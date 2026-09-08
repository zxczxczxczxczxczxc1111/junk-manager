# Сжатие баз SQLite
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.
#
# Образцы ОДНОСТРОЧНЫЕ намеренно. Многострочный образец сравнивается вместе с
# переводами строк, а .cs в рабочей копии и .ps1 рядом с ним не обязаны
# совпадать по CRLF и LF. Такая мутация не падает, она молча не находит
# образец, и счётчик показывает «поймано» там, где не проверено ничего.

@(
    @{ Imya = 'sqlite: Размер к освобождению берётся оценкой по freelist вместо замера'
       Fayl = 'src/JunkManager.Deletion/SqliteCompactor.cs'
       Iz   = 'var after = new FileInfo(probe).Length;'
       V    = 'var after = current - (WinSqlite.ScalarInt64(db, "PRAGMA freelist_count") * WinSqlite.ScalarInt64(db, "PRAGMA page_size"));' }

    @{ Imya = 'sqlite: Уборка временной копии замера перестаёт удалять файл'
       Fayl = 'src/JunkManager.Deletion/SqliteCompactor.cs'
       Iz   = 'File.Delete(path);'
       V    = '_ = path.Length;' }

    @{ Imya = 'sqlite: Занятая база в замере перестаёт объяснять причину пропуска'
       Fayl = 'src/JunkManager.Deletion/SqliteCompactor.cs'
       Iz   = '"база занята другим процессом или не открывается"'
       V    = '""' }

    @{ Imya = 'sqlite: Порог сжатия опускается до нуля, находкой становится любой байт'
       Fayl = 'src/JunkManager.Deletion/SqliteCompactSource.cs'
       Iz   = 'private const long Porog = 1024 * 1024;'
       V    = 'private const long Porog = 1;' }

    @{ Imya = 'sqlite: Пропуск занятой базы перестаёт называть держащий процесс'
       Fayl = 'src/JunkManager.Deletion/SqliteCompactSource.cs'
       Iz   = 'kto.Count > 0'
       V    = 'kto.Count > 100' }

    @{ Imya = 'sqlite: Находка сжатия выдаётся за опасную'
       Fayl = 'src/JunkManager.Deletion/SqliteCompactSource.cs'
       Iz   = 'Tier: RiskTier.Safe,'
       V    = 'Tier: RiskTier.Risk,' }

    @{ Imya = 'sqlite: Кандидат на сжатие перестаёт спрашивать guard'
       Fayl = 'src/JunkManager.Deletion/SqliteCompactSource.cs'
       Iz   = 'if (!SafetyGuard.TryVerify(candidate, out var verified, out var reason))'
       V    = 'if (!SafetyGuard.TryVerify(candidate, out var verified, out var reason) && false)' }

    @{ Imya = 'sqlite: Сжатие перестаёт перепроверять путь по разрешённому'
       Fayl = 'src/JunkManager.Deletion/SqliteVacuumExecutor.cs'
       Iz   = 'SafetyGuard.TryVerifyForDeletion(path.Value, out var checkedPath, out reason)'
       V    = 'SafetyGuard.TryVerify(path.Value, out var checkedPath, out reason)' }

    @{ Imya = 'sqlite: Отказ по занятой базе при сжатии перестаёт объяснять причину'
       Fayl = 'src/JunkManager.Deletion/SqliteVacuumExecutor.cs'
       Iz   = '"база занята другим процессом"'
       V    = '""' }
)

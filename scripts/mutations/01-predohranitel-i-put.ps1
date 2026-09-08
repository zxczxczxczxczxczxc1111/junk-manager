# Предохранитель, пути и правила
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'SafetyGuard всегда разрешает'
       Fayl = 'src/JunkManager.Safety/SafetyGuard.cs'
       Iz   = 'if (IsDenied(canonical, out var deniedBy) && !IsExplicitlyAllowed(canonical, deniedBy))'
       V    = 'if (false && IsDenied(canonical, out var deniedBy) && !IsExplicitlyAllowed(canonical, deniedBy))' }

    @{ Imya = 'Корень тома перестаёт быть запретом'
       Fayl = 'src/JunkManager.Safety/SafetyGuard.cs'
       Iz   = 'if (IsDeniedExact(canonical, out var exact))'
       V    = 'if (false && IsDeniedExact(canonical, out var exact))' }

    @{ Imya = 'Сравнение путей становится регистрозависимым'
       Fayl = 'src/JunkManager.Safety/SafetyGuard.cs'
       Iz   = 'StringComparison.OrdinalIgnoreCase)'
       V    = 'StringComparison.Ordinal)'
       Vse  = $true }

    @{ Imya = 'Вложенность подменяется префиксом'
       Fayl = 'src/JunkManager.Safety/SafetyGuard.cs'
       Iz   = 'return candidate.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);'
       V    = 'return candidate.StartsWith(r, StringComparison.OrdinalIgnoreCase);' }

    @{ Imya = 'Сверка разрешённого пути с объявленным снимается'
       Fayl = 'src/JunkManager.Safety/SafetyGuard.cs'
       Iz   = 'if (!canonicalResolved.Equals(declared.Value, StringComparison.OrdinalIgnoreCase))'
       V    = 'if (false && !canonicalResolved.Equals(declared.Value, StringComparison.OrdinalIgnoreCase))' }

    @{ Imya = 'Проверка недопустимых символов снимается'
       Fayl = 'src/JunkManager.Safety/SafetyGuard.cs'
       Iz   = 'if (trimmed.AsSpan().IndexOfAny(Nedopustimye) >= 0 || trimmed.Any(char.IsControl))'
       V    = 'if (false && (trimmed.AsSpan().IndexOfAny(Nedopustimye) >= 0 || trimmed.Any(char.IsControl)))' }

    @{ Imya = 'Предохранитель перестаёт требовать маркер'
       Fayl = 'src/JunkManager.Safety/VmFuse.cs'
       Iz   = 'string.Equals(envValue, "1", StringComparison.Ordinal) && markerExists'
       V    = 'string.Equals(envValue, "1", StringComparison.Ordinal)' }

    @{ Imya = 'Проверка reparse-точки при раскрытии снимается'
       Fayl = 'src/JunkManager.Core/Rules/PathExpander.cs'
       Iz   = 'if (IsReparsePoint(match))'
       V    = 'if (false && IsReparsePoint(match))' }

    @{ Imya = 'Неопределённая переменная подставляется выдуманным путём'
       Fayl = 'src/JunkManager.Core/Rules/PathExpander.cs'
       Iz   = 'var value = Environment.GetEnvironmentVariable(name);'
       V    = 'var value = Environment.GetEnvironmentVariable(name) ?? @"C:\jm-mutant";' }

    @{ Imya = 'Маска уходит на уровень глубже'
       Fayl = 'src/JunkManager.Core/Rules/PathExpander.cs'
       Iz   = 'SearchOption.TopDirectoryOnly)'
       V    = 'SearchOption.AllDirectories)'
       Vse  = $true }

    @{ Imya = 'Пустое consequence перестаёт быть ошибкой'
       Fayl = 'src/JunkManager.Core/Rules/RuleLoader.cs'
       Iz   = 'if (string.IsNullOrWhiteSpace(rule.Consequence))'
       V    = 'if (false)' }

    @{ Imya = 'Неизвестная ступень грузится как Safe'
       Fayl = 'src/JunkManager.Core/Rules/RuleLoader.cs'
       Iz   = '"Risk" => RiskTier.Risk,'
       V    = '"Risk" => RiskTier.Risk, "Review" => RiskTier.Safe,' }

    @{ Imya = 'Дубликат id правила перестаёт быть ошибкой'
       Fayl = 'src/JunkManager.Core/Rules/RuleLoader.cs'
       Iz   = 'if (seenIds.TryGetValue(rule.Id, out var firstSeenIn))'
       V    = 'if (false && seenIds.TryGetValue(rule.Id, out var firstSeenIn))' }

    @{ Imya = 'Сканер перестаёт спрашивать guard'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = 'if (!SafetyGuard.TryVerify(path, out var verified, out var reason))'
       V    = 'if (!SafetyGuard.TryVerify(path, out var verified, out var reason) && false)' }

    @{ Imya = 'Сканер заходит внутрь junction при подсчёте размера'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = 'if (new DirectoryInfo(sub).LinkTarget is null)'
       V    = 'if (true)' }

    @{ Imya = 'Отсечка по возрасту перестаёт работать'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = 'rule.OlderThanDays <= 0 || Dney(info.LastWriteTimeUtc, nowUtc) >= rule.OlderThanDays'
       V    = 'true' }

    @{ Imya = 'Отмена перестаёт быть видна в результате'
       Fayl = 'src/JunkManager.Core/Scanning/FileScanner.cs'
       Iz   = 'return new ScanResult(findings, skipped, Cancelled: true);'
       V    = 'return new ScanResult(findings, skipped, Cancelled: false);'
       Vse  = $true }

    @{ Imya = 'Кириллица в отчёте экранируется'
       Fayl = 'src/JunkManager.Core/Reporting/ScanJson.cs'
       Iz   = 'Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,'
       V    = '' }
)

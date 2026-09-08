# Права администратора
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд, потому что
# каждая ветка дописывала свои мутации в один и тот же конец массива.
# Новая задача заводит СВОЙ файл и не трогает чужие.
#
# Обе мутации с обёрткой ниже написаны так, чтобы КОМПИЛИРОВАТЬСЯ, и это стоило
# разбора. Обёртка вида `if (false && !TryProfileFromRegistry(sid, out var x, ...))`
# НЕ собирается: out-переменная правого операнда && перестаёт быть определённо
# присвоенной, и её использование ниже даёт CS0165. Замена текста причины на
# `reason = null` не собирается тоже: у параметра стоит [NotNullWhen(false)], и
# выход с false и null это CS8762. Обе такие мутации скрипт пометил бы
# «НЕ КОМПИЛИРУЕТСЯ», то есть не проверил бы ничего.

@(
    @{ Imya = 'Несовпадение переданного профиля перестаёт останавливать работу'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = @'
        if (!SafetyGuard.IsAtOrUnder(peredannyy, izReestra)
            && !izReestra.Equals(peredannyy, StringComparison.OrdinalIgnoreCase))
'@
       V    = @'
        if (false && !SafetyGuard.IsAtOrUnder(peredannyy, izReestra)
            && !izReestra.Equals(peredannyy, StringComparison.OrdinalIgnoreCase))
'@ }

    @{ Imya = 'Переданный SID игнорируется, читается профиль текущего процесса'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = 'zapis = koren.OpenSubKey($@"{ProfileList}\{sid}", writable: false);'
       V    = 'zapis = koren.OpenSubKey($@"{ProfileList}\{CurrentSid}", writable: false);' }

    @{ Imya = 'Отказ в UAC объявляется аварией'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = '            ? ElevationOutcome.Declined'
       V    = '            ? ElevationOutcome.Failed' }

    @{ Imya = 'Повышение происходит без просьбы'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = '(false, false) => ElevationOutcome.NotRequested,'
       V    = '(false, false) => ElevationOutcome.Relaunched,' }

    @{ Imya = 'Список прав отвечает «не требует» на Prefetch'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = 'ElevatedCapability.ReadPrefetch => true,'
       V    = 'ElevatedCapability.ReadPrefetch => false,' }

    @{ Imya = 'Половина переданной личности проходит за целую'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = 'if (sid.Length == 0 || profile.Length == 0)'
       V    = 'if (false && (sid.Length == 0 || profile.Length == 0))' }

    @{ Imya = 'Ответ «мы администратор» снова заводится вторым экземпляром'
       Fayl = 'src/JunkManager.Deletion/RebootDeleteScheduler.cs'
       Iz   = 'public static bool IsElevated => Elevation.IsElevated;'
       V    = 'public static bool IsElevated => !Elevation.IsElevated;' }
    @{ Imya = 'Переменные профиля строятся от текущего процесса'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = '["USERPROFILE"] = koren,'
       V    = '["USERPROFILE"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),' }

    @{ Imya = 'Хвостовой разделитель профиля перестаёт обрезаться'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = 'var koren = Path.TrimEndingDirectorySeparator(profilePath);'
       V    = 'var koren = profilePath;' }

    @{ Imya = 'Обрезка хвоста делается TrimEnd и ломает корень тома'
       Fayl = 'src/JunkManager.Safety/Elevation.cs'
       Iz   = 'var koren = Path.TrimEndingDirectorySeparator(profilePath);'
       V    = 'var koren = profilePath.TrimEnd(Path.DirectorySeparatorChar);' }
)

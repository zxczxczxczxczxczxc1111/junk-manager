# Обнаружители без списка и объяснение занятого места
#
# Файл возвращает массив мутаций. Свой файл на область: чужие не трогаем,
# иначе три ветки подряд дают конфликт на один и тот же конец массива.
#
# Имена подобраны так, чтобы область целиком поднималась двумя прогонами:
#   mutate.ps1 -Tolko 'обнаружител'   и   mutate.ps1 -Tolko 'объяснени'

@(
    @{ Imya = 'Обнаружитель кэшей перестаёт видеть список исключений'
       Fayl = 'src/JunkManager.Core/Sources/Detect/ElectronCacheDetector.cs'
       Iz   = 'if (DetectorExclusions.IsExcluded(Path.GetFileName(application))'
       V    = 'if (false && DetectorExclusions.IsExcluded(Path.GetFileName(application))' }

    @{ Imya = 'Обнаружитель кэшей заходит внутрь junction'
       Fayl = 'src/JunkManager.Core/Sources/Detect/ElectronCacheDetector.cs'
       Iz   = 'new DirectoryInfo(application).LinkTarget is not null'
       V    = '(false && new DirectoryInfo(application).LinkTarget is not null)' }

    @{ Imya = 'Обнаружитель кэшей перестаёт спрашивать guard'
       Fayl = 'src/JunkManager.Core/Sources/Detect/ElectronCacheDetector.cs'
       Iz   = 'if (!SafetyGuard.TryVerify(candidate, out var verified, out var reason))'
       V    = 'if (!SafetyGuard.TryVerify(candidate, out var verified, out var reason) && false)' }

    @{ Imya = 'Обнаружитель кэшей предлагает мелочь'
       Fayl = 'src/JunkManager.Core/Sources/Detect/ElectronCacheDetector.cs'
       Iz   = 'if (bytes < porog)'
       V    = 'if (false && bytes < porog)' }

    @{ Imya = 'Обнаружитель кэшей называет папку формы вместо приложения'
       Fayl = 'src/JunkManager.Core/Sources/Detect/ElectronCacheDetector.cs'
       Iz   = 'Name: $"Кэш {Path.GetFileName(application)}",'
       V    = 'Name: $"Кэш {new DirectoryInfo(candidate).Parent?.Name}",' }

    @{ Imya = 'Обнаружитель кэшей выдаёт кэш за опасную находку'
       Fayl = 'src/JunkManager.Core/Sources/Detect/ElectronCacheDetector.cs'
       Iz   = 'Tier: RiskTier.Safe,'
       V    = 'Tier: RiskTier.Risk,' }

    @{ Imya = 'Обнаружитель кэшей теряет одну из четырёх форм'
       Fayl = 'src/JunkManager.Core/Sources/Detect/ElectronCacheDetector.cs'
       Iz   = '["Code Cache", "js"],'
       V    = '["Code Cache", "js-takoy-formy-net"],' }

    @{ Imya = 'Обнаружитель смотрит на отметку каталога, а не на файлы внутри'
       Fayl = 'src/JunkManager.Core/Sources/Detect/ElectronCacheDetector.cs'
       Iz   = 'if (written > newest)'
       V    = 'if (false && written > newest)' }

    @{ Imya = 'Из списка исключений обнаружителей пропадает .ssh'
       Fayl = 'src/JunkManager.Core/Sources/Detect/DetectorExclusions.cs'
       Iz   = '        ".ssh", ".gnupg", ".aws",'
       V    = '        ".gnupg", ".aws",' }

    @{ Imya = 'Обнаружитель заброшенного перестаёт видеть список исключений'
       Fayl = 'src/JunkManager.Core/Sources/Detect/AbandonedFolderDetector.cs'
       Iz   = 'if (DetectorExclusions.IsExcluded(name)'
       V    = 'if (false && DetectorExclusions.IsExcluded(name)' }

    @{ Imya = 'Обнаружитель заброшенного заходит внутрь junction'
       Fayl = 'src/JunkManager.Core/Sources/Detect/AbandonedFolderDetector.cs'
       Iz   = 'new DirectoryInfo(candidate).LinkTarget is not null'
       V    = '(false && new DirectoryInfo(candidate).LinkTarget is not null)' }

    @{ Imya = 'Обнаружитель заброшенного берёт любой каталог, а не точечный'
       Fayl = 'src/JunkManager.Core/Sources/Detect/AbandonedFolderDetector.cs'
       Iz   = 'Directory.EnumerateDirectories(root, ".*", SearchOption.TopDirectoryOnly)'
       V    = 'Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)' }

    @{ Imya = 'Отсечка по возрасту у обнаружителя заброшенного снимается'
       Fayl = 'src/JunkManager.Core/Sources/Detect/AbandonedFolderDetector.cs'
       Iz   = 'if (idle < idleDays)'
       V    = 'if (false && idle < idleDays)' }

    @{ Imya = 'Обнаружитель заброшенного перестаёт спрашивать guard'
       Fayl = 'src/JunkManager.Core/Sources/Detect/AbandonedFolderDetector.cs'
       Iz   = 'if (!SafetyGuard.TryVerify(candidate, out var verified, out var reason))'
       V    = 'if (!SafetyGuard.TryVerify(candidate, out var verified, out var reason) && false)' }

    @{ Imya = 'Обнаружитель заброшенного предлагает мелочь'
       Fayl = 'src/JunkManager.Core/Sources/Detect/AbandonedFolderDetector.cs'
       Iz   = 'if (bytes < Porog)'
       V    = 'if (false && bytes < Porog)' }

    @{ Imya = 'Обнаружитель заброшенного выдаёт догадку за безопасную находку'
       Fayl = 'src/JunkManager.Core/Sources/Detect/AbandonedFolderDetector.cs'
       Iz   = 'Tier: RiskTier.Risk,'
       V    = 'Tier: RiskTier.Safe,' }

    @{ Imya = 'Объяснение диска считает вложенные находки дважды'
       Fayl = 'src/JunkManager.Core/Explain/DiskExplainer.cs'
       Iz   = 'if (prinyatye.Exists(shirokiy => SafetyGuard.Contains(shirokiy, proverennyy)))'
       V    = 'if (false && prinyatye.Exists(shirokiy => SafetyGuard.Contains(shirokiy, proverennyy)))' }

    @{ Imya = 'Объяснение диска печатает отрицательный остаток'
       Fayl = 'src/JunkManager.Core/Explain/DiskExplainer.cs'
       Iz   = 'Math.Max(0, UsedBytes - ExplainedBytes)'
       V    = 'UsedBytes - ExplainedBytes' }

    @{ Imya = 'Объяснение диска считает чужое за ссылкой в верхнем уровне'
       Fayl = 'src/JunkManager.Core/Explain/DiskExplainer.cs'
       Iz   = 'if (new DirectoryInfo(directory).LinkTarget is not null)'
       V    = 'if (false && new DirectoryInfo(directory).LinkTarget is not null)' }

    @{ Imya = 'Объяснение диска принимает нули за ответ тома'
       Fayl = 'src/JunkManager.Core/Interop/DiskSpace.cs'
       Iz   = 'if (!GetDiskFreeSpaceEx(root, out _, out var total, out var free))'
       V    = 'if (!GetDiskFreeSpaceEx(root, out _, out var total, out var free) && false)' }
)

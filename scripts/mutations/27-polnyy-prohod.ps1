# Общий проход по источникам
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Выключенный источник всё равно опрашивается'
       Fayl = 'src/JunkManager.Core/Scanning/PolnyyProhod.cs'
       Iz   = 'if (plan.PoiskPoObraztsu)'
       V    = 'if (true || plan.PoiskPoObraztsu)' }

    @{ Imya = 'Обнаружители перестают вызываться совсем'
       Fayl = 'src/JunkManager.Core/Scanning/PolnyyProhod.cs'
       Iz   = 'if (plan.Obnaruzhiteli)'
       V    = 'if (false && plan.Obnaruzhiteli)' }

    @{ Imya = 'Слияние источников заменяется сложением списков'
       Fayl = 'src/JunkManager.Core/Scanning/PolnyyProhod.cs'
       Iz   = 'itog = SliyanieIstochnikov.Slit(itog, chast.Findings, chast.Skipped);'
       V    = @'
            itog = itog with
            {
                Findings = [.. itog.Findings, .. chast.Findings],
                Skipped = [.. itog.Skipped, .. chast.Skipped],
            };
'@ }

    @{ Imya = 'Отмена прохода теряется по дороге'
       Fayl = 'src/JunkManager.Core/Scanning/PolnyyProhod.cs'
       Iz   = 'return otmeneno || ct.IsCancellationRequested ? itog with { Cancelled = true } : itog;'
       V    = 'return itog;' }

    @{ Imya = 'Отказ источника уносит с собой весь проход'
       Fayl = 'src/JunkManager.Core/Scanning/PolnyyProhod.cs'
       Iz   = @'
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException
                or RuleFormatException)
'@
       V    = @'
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException)
'@ }

    @{ Imya = 'Склад Windows снова ищет описания на уровень выше'
       Fayl = 'src/JunkManager.Core/Scanning/PolnyyProhod.cs'
       Iz   = @'
                new VolumeCacheSource().ScanAsync(
                    Path.Combine(rulesDirectory, KatalogIstochnikov), progress, ct))
'@
       V    = @'
                new VolumeCacheSource().ScanAsync(rulesDirectory, progress, ct))
'@ }
)

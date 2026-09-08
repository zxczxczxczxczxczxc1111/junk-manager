# Каркас приложения: решение о повышении до появления окна
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Ключ --no-elevate перестаёт перебивать настройку'
       Fayl = 'src/JunkManager.App/Startup/ElevationBootstrap.cs'
       Iz   = 'if (!wantElevation || args.Contains(NoElevateSwitch, StringComparer.OrdinalIgnoreCase))'
       V    = 'if (!wantElevation)' }

    @{ Imya = 'Перезапуск не передаёт --no-elevate и зацикливает запросы UAC'
       Fayl = 'src/JunkManager.App/Startup/ElevationBootstrap.cs'
       Iz   = 'var forwarded = args.Append(NoElevateSwitch).ToArray();'
       V    = 'var forwarded = args.ToArray();' }

    @{ Imya = 'Отказ от UAC в окне засчитывается за перезапуск'
       Fayl = 'src/JunkManager.App/Startup/ElevationBootstrap.cs'
       Iz   = @'
        return relaunch(forwarded)
            ? ElevationDecision.Relaunched
            : ElevationDecision.UserDeclined;
'@
       V    = @'
        return relaunch(forwarded)
            ? ElevationDecision.UserDeclined
            : ElevationDecision.Relaunched;
'@ }

    @{ Imya = 'Окно заводит свой ответ про права администратора'
       Fayl = 'src/JunkManager.App/Startup/ElevationBootstrap.cs'
       Iz   = 'internal static bool IsElevated => Elevation.IsElevated;'
       V    = 'internal static bool IsElevated => !Elevation.IsElevated;' }

    @{ Imya = 'Имя аргумента личности в окне расходится с CLI'
       Fayl = 'src/JunkManager.App/Startup/ElevationBootstrap.cs'
       Iz   = 'internal const string ArgumentSid = Elevation.ArgumentSid;'
       V    = 'internal const string ArgumentSid = "--sid";' }

    @{ Imya = 'Уже повышенный процесс просит повышения второй раз'
       Fayl = 'src/JunkManager.App/Startup/ElevationBootstrap.cs'
       Iz   = 'if (alreadyElevated)'
       V    = 'if (false && alreadyElevated)' }
)

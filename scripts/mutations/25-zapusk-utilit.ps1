# Запуск системных утилит
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'Системная утилита снова запускается по голому имени'
       Fayl = 'src/JunkManager.Core/Sources/Platform/ProcessRunner.cs'
       Iz   = @'
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), exeName);
'@
       V    = @'
        return exeName;
'@ }

    @{ Imya = 'Путь вместо имени утилиты перестаёт отвергаться'
       Fayl = 'src/JunkManager.Core/Sources/Platform/ProcessRunner.cs'
       Iz   = @'
        if (Path.IsPathRooted(exeName)
            || exeName.AsSpan().IndexOfAny(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) >= 0)
'@
       V    = @'
        if (false && Path.IsPathRooted(exeName))
'@ }
)

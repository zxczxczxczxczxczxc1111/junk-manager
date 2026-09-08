# Путь до деинсталлятора
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.

@(
    @{ Imya = 'msiexec снова зовётся по голому имени'
       Fayl = 'src/JunkManager.Core/Apps/UninstallCommandBuilder.cs'
       Iz   = @'
                ProcessRunner.SystemTool("msiexec.exe"),
'@
       V    = @'
                "msiexec.exe",
'@ }

    @{ Imya = 'Относительный путь до деинсталлятора снова проходит'
       Fayl = 'src/JunkManager.Core/Apps/UninstallCommandBuilder.cs'
       Iz   = 'if (Path.IsPathFullyQualified(exe))'
       V    = 'if (true || Path.IsPathFullyQualified(exe))' }

    @{ Imya = 'Проверка полного пути обходит ветку с кавычками'
       Fayl = 'src/JunkManager.Core/Apps/UninstallCommandBuilder.cs'
       Iz   = @'
            if (!PolnyyPut(ref exe, ref args, out reason))
            {
                return false;
            }

            reason = null;
            return true;
        }

        // Longest existing prefix
'@
       V    = @'
            reason = null;
            return true;
        }

        // Longest existing prefix
'@ }
)

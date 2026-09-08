# Точка восстановления
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.
#
# Номер 35, а не 33 как обещано в плане: 33 занят набором занятых файлов,
# 34 заводит план программ. Коллизия разобрана в отчёте задачи 1.

@(
    @{ Imya = 'Точка восстановления перестаёт требовать права администратора'
       Fayl = 'src/JunkManager.Safety/Native/SystemRestorePoint.cs'
       Iz   = 'if (!elevated)'
       V    = 'if (false && !elevated)' }

    @{ Imya = 'Пустое описание точки восстановления проходит'
       Fayl = 'src/JunkManager.Safety/Native/SystemRestorePoint.cs'
       Iz   = 'if (string.IsNullOrWhiteSpace(opisanie))'
       V    = 'if (false && string.IsNullOrWhiteSpace(opisanie))' }

    @{ Imya = 'Нулевой номер точки засчитывается за созданную точку'
       Fayl = 'src/JunkManager.Safety/Native/SystemRestorePoint.cs'
       Iz   = 'if (nomer == 0)'
       V    = 'if (false && nomer == 0)' }

    # Образец отличается от написанного в плане: там стояло opisanie.Length, а
    # в коде появилось celoe.Length. Причина в CA1062, разобрана в отчёте
    # задачи 1: открытый метод обязан проверять параметр на null, а бросать
    # здесь нельзя, пустое описание это штатный отказ словами.
    @{ Imya = 'Длинное описание уезжает в структуру целиком'
       Fayl = 'src/JunkManager.Safety/Native/SystemRestorePoint.cs'
       Iz   = 'var korotkoe = celoe.Length > MaxOpisaniya'
       V    = 'var korotkoe = celoe.Length > int.MaxValue' }
)

# Перебор находок реестра
#
# Файл возвращает массив мутаций. Список разрезан по файлам намеренно:
# один общий список стоил трёх конфликтов слияния подряд. Новая задача
# заводит СВОЙ файл и не трогает чужие.
#
# Номер 29, потому что это единственная свободная дырка в нумерации: 33, 34
# и 35 уже разобраны, причём 33 и 34 забронированы двумя планами каждый.

@(
    # Эта мутация на шаге 5 задачи 2 уронила ТРИ проверки, а не две, как ждал
    # план. Счётчик удалённых её НЕ ловит: находка вида «значение» уходит в
    # ветку ключа и сносит ветку целиком, удалённых по-прежнему одна. Ловит
    # только структурная проверка «ключ на месте, удаляли значение».
    @{ Imya = 'Маршрут значение-ключ перевёрнут'
       Fayl = 'src/JunkManager.Deletion/RegistryCleanupRunner.cs'
       Iz   = 'if (nahodka.Kind == RegistryEntryKind.Value)'
       V    = 'if (nahodka.Kind != RegistryEntryKind.Value)' }

    @{ Imya = 'Отмена перед находкой не проверяется'
       Fayl = 'src/JunkManager.Deletion/RegistryCleanupRunner.cs'
       Iz   = 'if (ct.IsCancellationRequested)'
       V    = 'if (false && ct.IsCancellationRequested)' }

    # Правится ПРИСВОЕНИЕ, а не отдача отчёта. Замена `otmeneno` на `false` в
    # return не компилируется: переменная остаётся присвоенной и никем не
    # читаемой, это CS0219, а при TreatWarningsAsErrors это провал сборки, то
    # есть мутация, которая не проверяет ничего. Проверено прогоном 06.09.2026.
    @{ Imya = 'Отмену засекли, но в отчёт она не попала'
       Fayl = 'src/JunkManager.Deletion/RegistryCleanupRunner.cs'
       Iz   = @'
                otmeneno = true;
                break;
'@
       V    = @'
                otmeneno = false;
                break;
'@ }

    # Отказ страховки это ПРОПУСК: продукт решил не трогать. Неудача это
    # «пытались и не вышло». Для человека это разные новости, и подмена одного
    # другим превращает запрет в поломку.
    @{ Imya = 'Отказ страховки значения приходит неудачей, а не пропуском'
       Fayl = 'src/JunkManager.Deletion/RegistryCleanupRunner.cs'
       Iz   = 'return new DeleteOutcome(nahodka.Address, DeleteStatus.Skipped, 0, otkaz);'
       V    = 'return new DeleteOutcome(nahodka.Address, DeleteStatus.Failed, 0, otkaz);' }

    # Отчёт ДО находки это единственное, из чего экран узнаёт адрес, пока
    # запись ещё удаляется. Без него строка хода пустая всё время работы и
    # заполняется задним числом.
    @{ Imya = 'Ход не сообщает адрес, пока запись ещё удаляется'
       Fayl = 'src/JunkManager.Deletion/RegistryCleanupRunner.cs'
       Iz   = @'
            progress?.Report(new RegistryCleanupProgress(
                ishody.Count, findings.Count, nahodka.Address));
'@
       V    = '' }
)

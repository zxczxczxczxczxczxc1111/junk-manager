#requires -Version 5.1
<#
    Вносит по одному дефекту в прод-код, прогоняет тесты и требует, чтобы набор
    ПОКРАСНЕЛ.

        powershell -ExecutionPolicy Bypass -File scripts\mutate.ps1

    Мутация, которую никто не поймал, это дыра в тестах, а не удача. Закрывается
    она НОВЫМ ТЕСТОМ, а не удалением мутации из списка. Список тут для того,
    чтобы требование «тесты обязаны уметь падать» стало машинно проверяемым.

    Каждая мутация соответствует настоящему классу дефектов, а не случайной
    правке символа: это не универсальный мутационный движок, это список тех
    ошибок, которые в чистильщике диска стоят дороже всего.

    ГРАБЛЯ, стоившая ложного отчёта «все 18 мутаций пойманы» 05.09.2026.
    При TreatWarningsAsErrors мутация вида `if (false)` НЕ СОБИРАЕТСЯ:
    недостижимый код это CS0162, неиспользуемое приватное поле это CA1823,
    непереданная out-переменная это CS0165. Сборка падает, код выхода ненулевой,
    и скрипт засчитывает это за пойманную мутацию, не проверив ничего.
    Поэтому: провал сборки отдаётся отдельным кодом 90, а мутации пишутся
    так, чтобы КОМПИЛИРОВАТЬСЯ и ломать именно поведение. Рабочий приём это
    `if (false && исходное_условие)`: ветка мертва во время работы, но
    выражение не константа, и компилятор молчит.
#>
[CmdletBinding()]
param(
    [switch]$OstanovitNaPervoyVyzhivshey,
    [string]$Tolko,
    [string]$Nabor
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$koren = Split-Path -Parent $PSScriptRoot

$katalogMutaciy = Join-Path $PSScriptRoot 'mutations'

# Список разрезан по файлам, по одному на область. Причина в слияниях: три
# ветки подряд дописывали свои мутации в конец одного массива и три раза дали
# конфликт на весь файл. Каталог эту цену убирает совсем.
$fayly = @(Get-ChildItem -Path $katalogMutaciy -Filter '*.ps1' | Sort-Object Name)
if ($fayly.Count -eq 0) {
    throw "в $katalogMutaciy нет ни одного файла мутаций"
}

# Отбор по файлу, а не по имени мутации. Нужен при работе над одной областью:
# полный набор это 228 мутаций и около четырёх часов, и всё это время дерево
# занято, а собрать проект нельзя. Отбор по -Tolko тут не годится: имена
# мутаций одной области общей подстроки не имеют и иметь не обязаны.
if ($Nabor) {
    $fayly = @($fayly | Where-Object { $_.Name -like "*$Nabor*" })
    if ($fayly.Count -eq 0) { throw "по образцу '$Nabor' файлов мутаций не нашлось" }
}

$mutacii = @()
foreach ($fayl in $fayly) {
    # Windows PowerShell 5.1 читает файл БЕЗ BOM как ANSI. Кириллица в именах
    # мутаций превращается в мусор, и падает это разбором синтаксиса: экран
    # заполняется «Unexpected token» и «The hash literal was incomplete», а
    # причина в трёх байтах в начале файла. Проверка стоит одну строку, разбор
    # без неё стоил получаса 05.09.2026.
    $pervye = [System.IO.File]::ReadAllBytes($fayl.FullName)
    if ($pervye.Length -lt 3 -or
        $pervye[0] -ne 0xEF -or $pervye[1] -ne 0xBB -or $pervye[2] -ne 0xBF) {
        throw ("файл мутаций $($fayl.Name) сохранён без BOM: PowerShell 5.1 " +
               "прочитает кириллицу как ANSI и упадёт разбором синтаксиса")
    }

    $chast = @(& $fayl.FullName)
    if ($chast.Count -eq 0) {
        # Пустой файл мутаций это тихо потерянная проверка, а счётчик внизу
        # покажет «все пойманы». Такое молчание дороже падения.
        throw "файл мутаций $($fayl.Name) не вернул ни одной мутации"
    }
    $mutacii += $chast
}

$dubli = @($mutacii | Group-Object { $_.Imya } | Where-Object { $_.Count -gt 1 })
if ($dubli.Count -gt 0) {
    # Одинаковые имена ломают -Tolko и отчёт: непонятно, какая из двух выжила.
    throw "повторяются имена мутаций: $($dubli.Name -join ', ')"
}


if ($Tolko) {
    $mutacii = @($mutacii | Where-Object { $_.Imya -like "*$Tolko*" })
    if ($mutacii.Count -eq 0) { throw "по образцу '$Tolko' мутаций не нашлось" }
}

$vyzhivshie = @()
$poymano = 0

# Кодировка ЯВНО на чтении и на записи. Set-Content по умолчанию берёт
# системную кодовую страницу, и кириллица в комментариях прод-кода
# превращается в мусор, который потом уезжает в коммит.
$kodirovka = New-Object System.Text.UTF8Encoding($false)

# Перед поиском И образец, И содержимое приводятся к одному концу строки.
# Причина, стоившая двух ложных «ОБРАЗЕЦ НЕ НАЙДЕН» 05.09.2026: файлы, пришедшие
# из слияния воркитри, лежат в CRLF, а файлы мутаций написаны в LF, и
# многострочный образец не совпадал ни с чем, хотя код был ровно тот. Мутируемый
# файл всё равно восстанавливается из исходных байтов, поэтому запись в LF
# ничего не портит.
function Odin-Konec([string]$tekst) { return $tekst -replace "`r`n", "`n" }


# ==== Восстановление после убитого прогона ====
#
# finally НЕ выполняется, когда процесс убивают. Проверено собой 06.09.2026:
# прогон был снят на середине, мутация «список запрещённых корней отключён»
# осталась в дереве, и следующий же запуск набора выполнил НАСТОЯЩЕЕ удаление
# каталога Windows. Снесло всё, где у администратора есть право удаления.
#
# Поэтому защита не в finally, а на диске: перед правкой рядом ложится копия
# файла и записка с путём. Записка переживает любую смерть процесса, и следующий
# запуск обязан начать с восстановления, а не с мутаций.
$katalogVosstanovleniya = Join-Path $PSScriptRoot '.mutate-vosstanovlenie'
$zapiska = Join-Path $katalogVosstanovleniya 'zapiska.txt'

function Vosstanovit-Ostavsheesya {
    if (-not (Test-Path $zapiska)) { return }

    $stroki = @(Get-Content -LiteralPath $zapiska -Encoding UTF8 | Where-Object { $_ })
    if ($stroki.Count -lt 2) {
        Remove-Item -LiteralPath $zapiska -Force
        return
    }

    $celevoy = $stroki[0]
    $kopiya = $stroki[1]

    if (Test-Path $kopiya) {
        Copy-Item -LiteralPath $kopiya -Destination $celevoy -Force
        Write-Host "ВОССТАНОВЛЕН после убитого прогона: $celevoy" -ForegroundColor Yellow
        Write-Host "Проверь git diff перед работой: прошлый прогон был снят на середине." -ForegroundColor Yellow
        Remove-Item -LiteralPath $kopiya -Force
    }

    Remove-Item -LiteralPath $zapiska -Force
}

if (-not (Test-Path $katalogVosstanovleniya)) {
    New-Item -ItemType Directory -Path $katalogVosstanovleniya -Force | Out-Null
}

Vosstanovit-Ostavsheesya

foreach ($m in $mutacii) {
    $put = Join-Path $koren $m.Fayl
    $ishodnyy = [IO.File]::ReadAllText($put, $kodirovka)

    $tekst = Odin-Konec $ishodnyy
    $obrazec = Odin-Konec $m.Iz
    $zamena = Odin-Konec $m.V

    if ($tekst.IndexOf($obrazec, [StringComparison]::Ordinal) -lt 0) {
        # Устаревшая мутация опаснее выжившей: она молча ничего не проверяет,
        # а счётчик показывает, что всё поймано.
        Write-Host "ОБРАЗЕЦ НЕ НАЙДЕН: $($m.Imya)" -ForegroundColor Yellow
        $vyzhivshie += "$($m.Imya): образец не найден, мутация устарела и ничего не проверяет"
        continue
    }

    $vse = $m.ContainsKey('Vse') -and $m.Vse

    if (-not $vse) {
        # Сколько раз образец встречается. Без 'Vse' правится ПЕРВОЕ вхождение,
        # и если первым идёт поясняющий комментарий, мутация правит комментарий,
        # поведение не меняется, а скрипт честно докладывает «ВЫЖИЛА». Час
        # разбора 05.09.2026 на PopupAnimation="None" ушёл ровно на это.
        # Неоднозначный образец не проверяет ничего, поэтому падаем сразу.
        $skolko = 0
        $poisk = 0
        while ($true) {
            $poisk = $tekst.IndexOf($obrazec, $poisk, [StringComparison]::Ordinal)
            if ($poisk -lt 0) { break }
            $skolko++
            $poisk += $obrazec.Length
        }

        if ($skolko -gt 1) {
            throw ("образец мутации '$($m.Imya)' встречается в $($m.Fayl) $skolko раз. " +
                   "Правится первое вхождение, и если первым идёт комментарий, мутация " +
                   "ничего не проверяет. Уточни образец или поставь Vse = `$true")
        }
    }

    $mutirovannyy = if ($vse) {
        $tekst.Replace($obrazec, $zamena)
    }
    else {
        $i = $tekst.IndexOf($obrazec, [StringComparison]::Ordinal)
        $tekst.Substring(0, $i) + $zamena + $tekst.Substring($i + $obrazec.Length)
    }

    # Копия и записка ложатся на диск ДО правки файла. Это единственное, что
    # переживает убийство процесса, см. Vosstanovit-Ostavsheesya выше.
    $kopiyaIshodnogo = Join-Path $katalogVosstanovleniya ([IO.Path]::GetFileName($put))
    [IO.File]::WriteAllText($kopiyaIshodnogo, $ishodnyy, $kodirovka)
    Set-Content -LiteralPath $zapiska -Value @($put, $kopiyaIshodnogo) -Encoding UTF8

    [IO.File]::WriteAllText($put, $mutirovannyy, $kodirovka)

    $kod = $null
    $prezhnyaya = $ErrorActionPreference
    try {
        # ErrorActionPreference опускается на время прогона НАМЕРЕННО. Мутация
        # может сделать прогон шумным в stderr (например, сломать уборку
        # песочницы), а Windows PowerShell 5.1 при 'Stop' превращает любую
        # строку stderr нативной команды в NativeCommandError и валит весь
        # скрипт. Инструмент, который умирает на середине списка от того, что
        # мутация сработала СЛИШКОМ хорошо, не инструмент.
        $ErrorActionPreference = 'Continue'
        & (Join-Path $PSScriptRoot 'run-tests.ps1') -Class Sandbox *>&1 | Out-Null
        $kod = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $prezhnyaya
        [IO.File]::WriteAllText($put, $ishodnyy, $kodirovka)
        Remove-Item -LiteralPath $zapiska -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $kopiyaIshodnogo -Force -ErrorAction SilentlyContinue
    }

    # Прогон не сообщил кода вовсе: считаем это красным, а не зелёным. Тихий
    # ноль на неизвестном исходе это ровно тот самообман, против которого
    # написан весь скрипт.
    if ($null -eq $kod) { $kod = 1 }

    if ($kod -eq 90) {
        # Мутация не компилируется, значит она НЕ ПРОВЕРИЛА НИЧЕГО. Засчитывать
        # её за пойманную нельзя: красный от компилятора и красный от теста это
        # разные вещи, и первый означает только то, что мутация написана плохо.
        Write-Host "НЕ КОМПИЛИРУЕТСЯ: $($m.Imya)" -ForegroundColor Yellow
        $vyzhivshie += "$($m.Imya): мутация не компилируется и ничего не проверяет"
    }
    elseif ($kod -eq 0) {
        Write-Host "ВЫЖИЛА: $($m.Imya)" -ForegroundColor Red
        $vyzhivshie += $m.Imya
        if ($OstanovitNaPervoyVyzhivshey) { break }
    }
    else {
        $poymano++
        Write-Host "поймана: $($m.Imya)" -ForegroundColor Green
    }
}

Write-Host ''
if ($vyzhivshie.Count -eq 0) {
    Write-Host "все $($mutacii.Count) мутаций пойманы" -ForegroundColor Green
    exit 0
}

Write-Host "НЕ ПОЙМАНО $($vyzhivshie.Count) из $($mutacii.Count), поймано $poymano" -ForegroundColor Red
$vyzhivshie | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
Write-Host ''
Write-Host 'Выжившая мутация закрывается НОВЫМ ТЕСТОМ, а не удалением её из списка.' -ForegroundColor Yellow
exit 1

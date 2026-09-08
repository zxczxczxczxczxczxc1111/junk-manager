#requires -Version 5.1
<#
    Единая точка запуска тестов.

        powershell -ExecutionPolicy Bypass -File scripts\run-tests.ps1 -Class Sandbox

    Классы описаны в разделе 14 спеки:
      Sandbox  свой временный каталог и своя ветка реестра, безопасно везде
      Live     настоящий %TEMP% и настоящие ветки автозапуска, только в госте
      LiveRead читает живую машину и ничего не портит, маркер не нужен
      Seeded   посеянный мусор и контрольная группа, только в госте
      Ui       собранное окно через FlaUI, гость с живым сеансом
      All      всё сразу

    Код выхода равен коду прогона. Ноль это зелёный прогон, всё остальное это
    падение, и молча его проглатывать нельзя.
#>
[CmdletBinding()]
param(
    [ValidateSet('Sandbox', 'Live', 'LiveRead', 'Seeded', 'Ui', 'All')]
    [string]$Class = 'Sandbox',

    [ValidateSet('Debug', 'Release')]
    [string]$Konfiguraciya = 'Debug',

    # Прогон без пересборки. Нужен mutate.ps1 не для скорости: он сам решает,
    # когда собирать, и лишняя сборка посреди мутации портит замер.
    [switch]$BezSborki
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$koren = Split-Path -Parent $PSScriptRoot
$artefakty = Join-Path $koren 'artifacts'
if (-not (Test-Path $artefakty)) { New-Item -ItemType Directory $artefakty -Force | Out-Null }

# Путь отчёта абсолютный. Относительный ложится туда, где случайно оказался
# рабочий каталог процесса, и один раз он уже насорил в корне репозитория.
$otchet = Join-Path $artefakty 'test-report.trx'

# Настоящие ключи раннера xunit.v3 4.0.0: -trait и -result-trx.
# В плане были написаны --filter-trait и -result-trx, первый не существует
# вовсе. Проверено выводом самого раннера по ключу -?.
$argumenty = switch ($Class) {
    'Sandbox'  { @('-trait', 'Class=Sandbox') }
    'Live'     { @('-trait', 'Class=LiveDestructive') }
    'LiveRead' { @('-trait', 'Class=LiveRead') }
    'Seeded'   { @('-trait', 'Class=Seeded') }
    'Ui'       { @('-trait', 'Class=Ui') }
    'All'      { @() }
}
$argumenty += @('-result-trx', $otchet)

# Собирается РЕШЕНИЕ целиком, а не один тестовый проект. Проверено фактом
# 05.09.2026: сборка только tests\JunkManager.Tests тянет за собой Core, Safety и
# Deletion, но НЕ трогает JunkManager.Cli, потому что на него никто не ссылается.
# Несобираемый CLI при этом давал зелёный прогон, то есть ворота пропускали
# сломанный продукт.
$reshenie = Join-Path $koren 'JunkManager.slnx'
$proekt = Join-Path $koren 'tests\JunkManager.Tests'

Push-Location $koren
try {
    if (-not $BezSborki) {
        & dotnet build $reshenie -c $Konfiguraciya --nologo -v q
        if ($LASTEXITCODE -ne 0) {
            # Отдельный код, а не код компилятора. Иначе mutate.ps1 не отличает
            # "тесты покраснели" от "мутация не компилируется", и вторая
            # засчитывается как пойманная. Скрипт, который пишет "все мутации
            # пойманы", не проверив ни одной, это самообман ровно того сорта,
            # против которого он и написан.
            Write-Host "СБОРКА УПАЛА, код $LASTEXITCODE" -ForegroundColor Red
            exit 90
        }
    }

    & dotnet run --project $proekt -c $Konfiguraciya --no-build -- @argumenty
    $kod = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($kod -ne 0) {
    Write-Host "ТЕСТЫ УПАЛИ, код $kod. Отчёт: $otchet" -ForegroundColor Red
}
else {
    Write-Host "тесты прошли. Отчёт: $otchet" -ForegroundColor Green
}

exit $kod

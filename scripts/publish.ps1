#requires -Version 5.1
<#
    Собирает приложение и CLI со встроенным каталогом Core.
    На хосте этот скрипт только собирает и копирует. Тесты и проверка
    исполняемых файлов выполняются отдельно в VM junkmanager-stend.
#>
[CmdletBinding()]
param(
    # Compatibility switch: tests no longer audition on the host at all.
    [switch]$BezTestov,
    [string]$Kuda,
    [switch]$CleanLegacyRules
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NormalizedDirectory([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'пустой путь выходного каталога'
    }
    return [IO.Path]::GetFullPath($Path).TrimEnd([char[]]'\/')
}

function Test-ChildDirectory([string]$Path, [string]$Root) {
    return $Path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparseAncestors([string]$Path) {
    $current = $Path
    while ($current) {
        try {
            $attributes = [IO.File]::GetAttributes($current)
            if ($attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "выходной каталог содержит ссылку: $current"
            }
        }
        catch [IO.FileNotFoundException] {
            # Missing output is fine; a broken link still has reparse attributes.
        }
        catch [IO.DirectoryNotFoundException] {
            # Its existing parents still need checking. Filesystems enjoy surprises.
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Remove-CheckedDirectory([string]$Target, [string]$Boundary) {
    $targetPath = Get-NormalizedDirectory $Target
    $boundaryPath = Get-NormalizedDirectory $Boundary
    if (-not (Test-ChildDirectory $targetPath $boundaryPath)) {
        throw "отказ удаления вне ожидаемого выхода: $targetPath; граница $boundaryPath"
    }
    Assert-NoReparseAncestors $targetPath
    if (-not (Test-Path -LiteralPath $targetPath)) { return }
    if (-not (Get-Item -LiteralPath $targetPath -Force).PSIsContainer) {
        throw "ожидался каталог: $targetPath"
    }

    # Preflight the whole tree before removing anything. No recursive walker gets a leash.
    $pending = [Collections.Generic.Stack[string]]::new()
    $entries = [Collections.Generic.List[string]]::new()
    $pending.Push($targetPath)
    $entries.Add($targetPath)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        Assert-NoReparseAncestors $directory
        foreach ($entry in Get-ChildItem -LiteralPath $directory -Force) {
            $entryPath = Get-NormalizedDirectory $entry.FullName
            if (-not (Test-ChildDirectory $entryPath $targetPath)) {
                throw "элемент вышел за границу уборки: $entryPath"
            }
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "в выходном каталоге найдена ссылка, уборка отменена: $entryPath"
            }
            $entries.Add($entryPath)
            if ($entry.PSIsContainer) { $pending.Push($entryPath) }
        }
    }

    for ($index = $entries.Count - 1; $index -ge 0; $index--) {
        $entryPath = $entries[$index]
        Assert-NoReparseAncestors $entryPath
        $entry = Get-Item -LiteralPath $entryPath -Force
        if ($entry.PSIsContainer) {
            # false is load-bearing: a concurrent newcomer causes failure, never recursion.
            [IO.Directory]::Delete($entryPath, $false)
        }
        else {
            Remove-Item -LiteralPath $entryPath -Force
        }
    }
}

$koren = Get-NormalizedDirectory (Split-Path -Parent $PSScriptRoot)
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $koren 'artifacts'))

if ($CleanLegacyRules) {
    # MSBuild passes data through the environment, not through a shell expression.
    $output = Get-NormalizedDirectory $env:JUNKMANAGER_LEGACY_OUTPUT
    $kind = $env:JUNKMANAGER_LEGACY_KIND
    $testBin = Join-Path $koren 'tests\JunkManager.Tests\bin'
    switch ($kind) {
        'App' { $ownBin = Join-Path $koren 'src\JunkManager.App\bin'; $marker = 'JunkManager.exe'; $child = 'app' }
        'Cli' { $ownBin = Join-Path $koren 'src\JunkManager.Cli\bin'; $marker = 'JunkManager.Cli.exe'; $child = 'cli' }
        'Tests' { $ownBin = $testBin; $marker = 'JunkManager.Tests.exe'; $child = '' }
        default { throw "неизвестный вид выходного каталога: $kind" }
    }
    $allowed = (Test-ChildDirectory $output $ownBin) -or (Test-ChildDirectory $output $artifactsRoot)
    if ($kind -ne 'Tests') {
        $allowed = $allowed -or ((Test-ChildDirectory $output $testBin) -and
            ([IO.Path]::GetFileName($output) -eq $child))
    }
    if (-not $allowed) {
        throw "уборка правил разрешена только в ожидаемом bin или artifacts: $output"
    }
    Assert-NoReparseAncestors $output
    $markerPath = Join-Path $output $marker
    Assert-NoReparseAncestors $markerPath
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "выход не подтверждён собранным файлом $markerPath"
    }
    if ($kind -eq 'Tests') {
        # The test suite keeps its own rules fixtures. Only packaged products lose theirs.
        Remove-CheckedDirectory (Join-Path $output 'app\rules') (Join-Path $output 'app')
        Remove-CheckedDirectory (Join-Path $output 'cli\rules') (Join-Path $output 'cli')
    }
    else {
        Remove-CheckedDirectory (Join-Path $output 'rules') $output
    }
    exit 0
}

$vyhod = Get-NormalizedDirectory $(if ($Kuda) { $Kuda } else { Join-Path $artifactsRoot 'publish' })

# Deletion only visits generated artifacts; the rest of the disk has suffered enough.
if (-not $vyhod.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "каталог публикации должен лежать внутри $artifactsRoot, получен $vyhod"
}
Remove-CheckedDirectory $vyhod $artifactsRoot

Write-Host '== Собираю выкладку ==' -ForegroundColor Cyan

# Self-contained: у человека может не быть ни одного рантайма .NET, и «скачайте
# сначала вот это» превращает чистильщик диска в задачу на полчаса.
# PublishSingleFile includes the catalog too; loose rules have lost their keys.
& dotnet publish (Join-Path $koren 'src\JunkManager.App\JunkManager.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $vyhod

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish упал с кодом $LASTEXITCODE"
}

# CLI ships alongside the app; neither needs a folder of editable instructions.
Write-Host '== Собираю утилиту командной строки ==' -ForegroundColor Cyan

& dotnet publish (Join-Path $koren 'src\JunkManager.Cli\JunkManager.Cli.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $vyhod

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish утилиты упал с кодом $LASTEXITCODE"
}

$exe = Join-Path $vyhod 'JunkManager.exe'
$cli = Join-Path $vyhod 'JunkManager.Cli.exe'

if (-not (Test-Path $cli)) {
    throw "утилиты командной строки в выкладке нет, а README на неё ссылается"
}

if (-not (Test-Path $exe)) {
    throw "выкладка прошла, а $exe нет: имя сборки разошлось с ожиданием"
}

if (Test-Path -LiteralPath (Join-Path $vyhod 'rules')) {
    throw 'в поставку попал внешний каталог правил, каталог должен быть встроен в Core'
}

$razmer = [math]::Round((Get-Item $exe).Length / 1MB, 1)
$hesh = (Get-FileHash -Algorithm SHA256 $exe).Hash

Write-Host ''
Write-Host '== Выложено ==' -ForegroundColor Green
Write-Host "  файл    $exe"
Write-Host "  размер  $razmer МБ"
Write-Host "  SHA256  $hesh"
Write-Host "  каталог встроен в Core"
Write-Host "  утилита $cli"
Write-Host ''

# Runtime validation belongs in junkmanager-stend, not in the build script.
foreach ($document in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.txt', 'RELEASE_NOTES.md')) {
    Copy-Item -LiteralPath (Join-Path $koren $document) -Destination (Join-Path $vyhod $document)
}

Write-Host ''
Write-Host 'Проверить в junkmanager-stend: rules-validate, тесты и запуск окна; на хосте не запускать.'
Write-Host 'Сборка НЕ подписана, поэтому SmartScreen на чужой машине покажет'
Write-Host 'предупреждение при первом запуске.'

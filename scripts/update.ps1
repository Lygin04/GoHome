<#
.SYNOPSIS
    Обновляет установленный GoHome до последнего релиза.

.DESCRIPTION
    Единственный способ обновления: само приложение в сеть не ходит и о релизах
    ничего не знает. Скрипт находит последний релиз, скачивает бинарник, сверяет
    контрольную сумму, останавливает приложение, заменяет файл и запускает обратно.

    Несовпадение суммы прекращает работу, ничего не изменив.

.NOTES
    Права администратора не нужны. Путь установки не меняется, поэтому задачу
    планировщика трогать не нужно — она продолжает запускать тот же файл.

    Скрипт устаревает вместе с версией: он ставится рядом с приложением при установке
    и сам себя не обновляет. Поэтому он опирается только на то, что release.yml держит
    неизменным от версии к версии: имя ассета вида GoHome-<версия>-win-x64.exe,
    файл SHA256SUMS.txt и путь установки.
#>
[CmdletBinding()]
param(
    # Репозиторий, откуда берутся релизы.
    [string] $Repository = 'Lygin04/GoHome',

    # Каталог установки. Тот же, что прописывает install.cmd.
    [string] $Target = (Join-Path $env:LOCALAPPDATA 'Programs\GoHome'),

    # Поставить релиз, даже если он не новее установленного.
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

# PowerShell 5.1 по умолчанию не умеет TLS 1.2, а GitHub другого не принимает.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$exe = Join-Path $Target 'GoHome.exe'

function Fail([string] $message) {
    Write-Host ''
    Write-Host $message -ForegroundColor Red
    Write-Host ''
    exit 1
}

# «1.2» и «1.2.0.0» — одна и та же версия. Без выравнивания они сравниваются как разные.
function Normalize([Version] $value) {
    [Version]::new($value.Major, $value.Minor, [Math]::Max($value.Build, 0), [Math]::Max($value.Revision, 0))
}

if (-not (Test-Path -LiteralPath $exe)) {
    Fail "GoHome не установлен в $Target. Сначала запустите install.cmd."
}

$installed = Normalize ([Version] (Get-Item -LiteralPath $exe).VersionInfo.FileVersion)
Write-Host "Установлена версия $installed"

Write-Host 'Спрашиваю GitHub про последний релиз...'
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" `
        -Headers @{ 'User-Agent' = 'GoHome-update-script' } -TimeoutSec 60
} catch {
    Fail "Не удалось получить список релизов: $($_.Exception.Message)"
}

# Префикс «v» в теге необязателен, предрелизы вида 1.2.0-rc1 не ставим.
$tag = [string] $release.tag_name
$text = $tag -replace '^[vV]', ''
if ($text -notmatch '^\d+(\.\d+){1,3}$') {
    Fail "Тег '$tag' не похож на версию — ставить нечего."
}

$latest = Normalize ([Version] $text)
Write-Host "Последний релиз: $latest"

# Сравнение числовое: как строки «0.10.0» меньше «0.9.0».
if (-not $Force -and $latest -le $installed) {
    Write-Host ''
    Write-Host 'Установлена последняя версия, обновлять нечего.' -ForegroundColor Green
    Write-Host ''
    exit 0
}

# Имя ассета содержит версию, поэтому файл ищется по маске, а не по зашитой ссылке.
$binary = $release.assets | Where-Object { $_.name -like 'GoHome-*-win-x64.exe' } | Select-Object -First 1
$sums = $release.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1

if (-not $binary -or -not $sums) {
    Fail "К релизу $tag не приложены бинарник и файл сумм."
}

# Качаем во временное имя рядом с установленным файлом: пока сумма не сошлась,
# рабочий GoHome.exe остаётся нетронутым.
$partial = "$exe.new"

Write-Host "Скачиваю $($binary.name) ..."
try {
    Invoke-WebRequest -Uri $binary.browser_download_url -OutFile $partial -UseBasicParsing -TimeoutSec 1200
    $raw = (Invoke-WebRequest -Uri $sums.browser_download_url -UseBasicParsing -TimeoutSec 60).Content
} catch {
    Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
    Fail "Загрузка не удалась: $($_.Exception.Message)"
}

# GitHub отдаёт файл сумм как application/octet-stream, и PowerShell 5.1 кладёт
# в .Content массив байт, а не строку. Разбор такого «текста» по строкам молча даёт
# мусор, ни одна строка не совпадает с именем файла — и целая загрузка отвергается
# как битая. Декодируем явно.
$expected = if ($raw -is [byte[]]) { [Text.Encoding]::UTF8.GetString($raw) } else { [string] $raw }

Write-Host 'Сверяю контрольную сумму...'
$actual = (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash
$line = $expected -split "`n" | Where-Object { $_ -match [regex]::Escape($binary.name) } | Select-Object -First 1
$wanted = if ($line) { ($line.Trim() -split '\s+')[0] } else { $null }

if (-not $wanted -or $wanted -ne $actual) {
    # Оборванная загрузка не должна превратиться в неработающую программу.
    Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
    Fail 'Контрольная сумма не совпала. Скачанный файл удалён, установленная версия не тронута.'
}

# Останавливаем только свой экземпляр: чужой GoHome.exe из другого каталога не наш.
$running = @(Get-Process -Name 'GoHome' -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -eq $exe } catch { $false }
})

if ($running.Count -gt 0) {
    Write-Host 'Останавливаю приложение...'
    foreach ($process in $running) {
        # Убивать безопасно: отметки пишутся сразу в файл дня, накопленное не теряется.
        try { Stop-Process -Id $process.Id -Force -ErrorAction Stop } catch { }

        # Ждём фактического завершения: файл ещё занят, пока процесс не умер.
        if (-not $process.WaitForExit(15000)) {
            Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
            Fail "Процесс GoHome (PID $($process.Id)) не завершился. Установленная версия не тронута."
        }
    }
} else {
    Write-Host 'Приложение не запущено.'
}

Write-Host 'Заменяю файл...'
try {
    Move-Item -LiteralPath $partial -Destination $exe -Force
} catch {
    Fail "Не удалось заменить GoHome.exe: $($_.Exception.Message)"
}

Write-Host 'Запускаю...'
Start-Process -FilePath $exe -WorkingDirectory $Target | Out-Null

Write-Host ''
Write-Host "Готово: $installed -> $latest" -ForegroundColor Green
Write-Host 'Значок-кольцо снова в трее, накопленное за день время на месте.'
Write-Host ''
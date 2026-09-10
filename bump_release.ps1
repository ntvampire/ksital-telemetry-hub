param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Type = "patch",
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"

# Получаем свежие теги с сервера
git fetch --tags

# Ищем последний тег версии
$latestTag = git tag -l "v*.*.*" --sort=-v:refname | Select-Object -First 1

if (-not $latestTag) {
    $latestTag = "v1.0.0"
}

$cleanVer = $latestTag.TrimStart('v')
$parts = $cleanVer.Split('.')
[int]$major = $parts[0]
[int]$minor = $parts[1]
[int]$patch = $parts[2]

switch ($Type) {
    "patch" { $patch++ }
    "minor" { $minor++; $patch = 0 }
    "major" { $major++; $minor = 0; $patch = 0 }
}

$nextVer = "$major.$minor.$patch"
$nextTag = "v$nextVer"

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Текущий релиз: $latestTag" -ForegroundColor DarkGray
Write-Host " Готовится:     $nextTag ($Type)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""

# Запрос описания изменений, если не передано в параметрах
if ([string]::IsNullOrWhiteSpace($Notes)) {
    Write-Host "Введите описание изменений для релиза $nextTag (Enter для значения по умолчанию):" -ForegroundColor Yellow
    $Notes = Read-Host ">"
}

if ([string]::IsNullOrWhiteSpace($Notes)) {
    $Notes = "Плановое обновление системы ($Type) $nextTag"
}

# Обновляем версию в UI.Desktop.csproj
$projPath = "src\UI.Desktop\UI.Desktop.csproj"
$projXml = [xml](Get-Content $projPath -Raw)
$verNode = $projXml.SelectSingleNode("//Version")
if ($verNode) {
    $verNode.InnerText = $nextVer
    $projXml.Save((Resolve-Path $projPath))
}

Write-Host "`n1. Фиксация изменений в ветке main..." -ForegroundColor Yellow
git add $projPath
git commit -m "chore: bump version to $nextTag"
git push origin main

Write-Host "`n2. Создание тега с описанием изменений..." -ForegroundColor Yellow
# Записываем описание прямо в сообщение аннотированного тега
git tag -a $nextTag -m "$Notes"
git push origin $nextTag

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " Релиз $nextTag успешно запущен!" -ForegroundColor Green
Write-Host " Описание: $Notes" -ForegroundColor White
Write-Host " GitHub Actions собирает установщик..." -ForegroundColor Yellow
Write-Host "========================================================" -ForegroundColor Green
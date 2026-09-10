param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Type = "patch"
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

Write-Host "Текущий релиз: $latestTag" -ForegroundColor Cyan
Write-Host "Новый релиз:   $nextTag" -ForegroundColor Green

# Обновляем тег Version в проекте UI.Desktop.csproj
$projPath = "src\UI.Desktop\UI.Desktop.csproj"
$projXml = [xml](Get-Content $projPath -Raw)
$verNode = $projXml.SelectSingleNode("//Version")
if ($verNode) {
    $verNode.InnerText = $nextVer
    $projXml.Save((Resolve-Path $projPath))
}

# Фиксируем обновление версии
git add $projPath
git commit -m "chore: bump release version to $nextTag"
git push origin main

# Ставим и отправляем тег
git tag -a $nextTag -m "Release $nextTag"
git push origin $nextTag

Write-Host "Тег $nextTag успешно отправлен. GitHub Actions начал автосборку релиза!" -ForegroundColor Yellow
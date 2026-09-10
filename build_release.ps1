param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Build Ksital Hub v$Version ===" -ForegroundColor Cyan

$distDir = Join-Path $PSScriptRoot "dist\ksital-hub"
$zipOutput = Join-Path $PSScriptRoot "dist\ksital-hub-v$Version.zip"

if (Test-Path (Join-Path $PSScriptRoot "dist")) {
    Remove-Item (Join-Path $PSScriptRoot "dist") -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "1/4. Publish UI.Desktop (win-x64)..." -ForegroundColor Yellow
$uiProj = Join-Path $PSScriptRoot "src\UI.Desktop\UI.Desktop.csproj"
dotnet publish $uiProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $distDir

Write-Host "2/4. Publish Service.Worker (win-x64)..." -ForegroundColor Yellow
$workerDir = Join-Path $distDir "WorkerService"
$workerProj = Join-Path $PSScriptRoot "src\Service.Worker\Service.Worker.csproj"
dotnet publish $workerProj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $workerDir

Write-Host "3/4. Copy deployment scripts and cleanup..." -ForegroundColor Yellow
Get-ChildItem -Path $distDir -Include "telemetry.db*","*.pdb" -Recurse | Remove-Item -Force

Copy-Item (Join-Path $PSScriptRoot "src\install.bat") -Destination $distDir -Force
Copy-Item (Join-Path $PSScriptRoot "src\start_ui.bat") -Destination $distDir -Force

Write-Host "4/4. Packaging ZIP..." -ForegroundColor Yellow
Compress-Archive -Path "$distDir\*" -DestinationPath $zipOutput -Force

Write-Host "SUCCESS: $zipOutput" -ForegroundColor Green
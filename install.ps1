<#
.SYNOPSIS
  Builds Marleo from source, installs it for the current user and adds it to Windows Explorer.

.DESCRIPTION
  - Publishes to %LOCALAPPDATA%\Programs\marleo (no admin rights needed).
  - Runs `marleo --register`: "Open with Marleo" on folders and .md files, `marleo` (and `mdv`) on PATH and in Explorer's address bar.
  Requirements: .NET 10 SDK and Node.js (only to fetch the front-end libraries the first time).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1 -NoRegister
#>
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA "Programs\marleo"),
    [switch]$NoRegister
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if (-not (Test-Path "web\vendor\markdown-it.min.js")) {
    Write-Host "Fetching front-end libraries (npm ci + vendor)..." -ForegroundColor Cyan
    npm ci --no-audit --no-fund
    if ($LASTEXITCODE) { throw "npm ci failed" }
    npm run vendor
    if ($LASTEXITCODE) { throw "npm run vendor failed" }
}

$running = Get-Process marleo -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Destination*" }
if ($running) {
    Write-Host "Closing running Marleo windows so they can be updated..." -ForegroundColor Yellow
    $running | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    Start-Sleep -Milliseconds 800
    $running | Where-Object { -not $_.HasExited } | Stop-Process -Force
}

Write-Host "Publishing to $Destination ..." -ForegroundColor Cyan
dotnet publish src\Marleo\Marleo.csproj -c Release -o $Destination -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish failed" }

if (-not $NoRegister) {
    & (Join-Path $Destination "marleo.exe") --register --quiet | Out-Null
    Write-Host ""
    Write-Host "Marleo is installed and registered with Explorer." -ForegroundColor Green
    Write-Host "  - Right-click a folder -> Show more options -> Open with Marleo"
    Write-Host "  - Right-click a .md file -> Open with -> Marleo -> Always   (makes double-click open Marleo)"
    Write-Host "  - Type 'marleo' in Explorer's address bar, or 'marleo <path>' in any new terminal ('mdv' still works too)"
} else {
    Write-Host "Marleo published to $Destination (not registered)." -ForegroundColor Green
}

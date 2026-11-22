<#
.SYNOPSIS
  Builds mdv from source, installs it for the current user and adds it to Windows Explorer.

.DESCRIPTION
  - Publishes to %LOCALAPPDATA%\Programs\mdv (no admin rights needed).
  - Runs `mdv --register`: "Open with mdv" on folders and .md files, `mdv` on PATH and in Explorer's address bar.
  Requirements: .NET 10 SDK and Node.js (only to fetch the front-end libraries the first time).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File install.ps1 -NoRegister
#>
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA "Programs\mdv"),
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

$running = Get-Process mdv -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Destination*" }
if ($running) {
    Write-Host "Closing running mdv windows so they can be updated..." -ForegroundColor Yellow
    $running | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    Start-Sleep -Milliseconds 800
    $running | Where-Object { -not $_.HasExited } | Stop-Process -Force
}

Write-Host "Publishing to $Destination ..." -ForegroundColor Cyan
dotnet publish src\Mdv\Mdv.csproj -c Release -o $Destination -nologo -v q
if ($LASTEXITCODE) { throw "dotnet publish failed" }

if (-not $NoRegister) {
    & (Join-Path $Destination "mdv.exe") --register --quiet | Out-Null
    Write-Host ""
    Write-Host "mdv is installed and registered with Explorer." -ForegroundColor Green
    Write-Host "  - Right-click a folder -> Show more options -> Open with mdv"
    Write-Host "  - Right-click a .md file -> Open with -> mdv -> Always   (makes double-click open mdv)"
    Write-Host "  - Type 'mdv' in Explorer's address bar, or 'mdv <path>' in any new terminal"
} else {
    Write-Host "mdv published to $Destination (not registered)." -ForegroundColor Green
}

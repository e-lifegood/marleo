<#
.SYNOPSIS
  End-to-end tests for Marleo: drives the real app (WebView2) and checks the results.

.DESCRIPTION
  1. UI smoke test   - highlights, Alt+n navigation, find, outline, code, alerts, tasks, images, Mermaid, theme.
  2. Live reload     - direct write, "temp file + rename" (how editors/agents save) and delete + recreate.
  Screenshots are written to tests\out\ so you can eyeball the result.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
#>
param([string]$Configuration = "Debug")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

dotnet build src\Marleo\Marleo.csproj -c $Configuration -v q -nologo | Out-Null
if ($LASTEXITCODE) { throw "build failed" }
$exe = (Resolve-Path "src\Marleo\bin\$Configuration\net10.0-windows\win-x64\marleo.exe").Path
$out = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $out | Out-Null
$log = Join-Path $env:LOCALAPPDATA "marleo\marleo.log"
$failed = 0

function Read-Log { if (Test-Path $log) { Get-Content $log -Encoding UTF8 } else { @() } }

# --- 1. UI smoke test ----------------------------------------------------------------
Remove-Item $log -ErrorAction SilentlyContinue
$p = Start-Process $exe -ArgumentList 'samples\demo.md', '--window', '1600x1000', '--run-script', 'tests\ui-smoke.js', '--screenshot', "$out\smoke.png" -PassThru
if (-not $p.WaitForExit(60000)) { $p.Kill(); throw "smoke test timed out" }
$line = Read-Log | Where-Object { $_ -match "SMOKE " } | Select-Object -Last 1
if (-not $line) { Write-Host "FAIL  smoke test produced no results" -ForegroundColor Red; $failed++ }
else {
    $results = ($line -split "SMOKE ", 2)[1] | ConvertFrom-Json
    foreach ($r in $results) {
        if ($r.ok) { Write-Host ("PASS  {0}  {1}" -f $r.name, $r.detail) -ForegroundColor Green }
        else { Write-Host ("FAIL  {0}  {1}" -f $r.name, $r.detail) -ForegroundColor Red; $failed++ }
    }
}

# --- 2. live reload ------------------------------------------------------------------
$tmp = Join-Path $out "live"
New-Item -ItemType Directory -Force $tmp | Out-Null
$f = Join-Path $tmp "live.md"
$utf8 = New-Object Text.UTF8Encoding $false
[IO.File]::WriteAllText($f, "# Version 1`n`none`n", $utf8)
Remove-Item $log -ErrorAction SilentlyContinue
$p = Start-Process $exe -ArgumentList "`"$f`"", '--run-script', 'tests\reload-probe.js' -PassThru
Start-Sleep -Seconds 4
[IO.File]::WriteAllText($f, "# Version 2`n`none`n`ntwo`n", $utf8)
Start-Sleep -Milliseconds 1500
[IO.File]::WriteAllText("$f.tmp", "# Version 3`n`na`n`nb`n`nc`n", $utf8)
Move-Item "$f.tmp" $f -Force
Start-Sleep -Milliseconds 1500
Remove-Item $f
Start-Sleep -Milliseconds 800
[IO.File]::WriteAllText($f, "# Version 4`n", $utf8)
Start-Sleep -Milliseconds 1500
$p.CloseMainWindow() | Out-Null
if (-not $p.WaitForExit(5000)) { $p.Kill() }

$text = (Read-Log) -join "`n"
foreach ($case in @(@("direct write", "h1=Version 2"), @("temp file + rename", "h1=Version 3"), @("delete + recreate", "h1=Version 4"))) {
    if ($text -match [regex]::Escape($case[1])) { Write-Host "PASS  live reload: $($case[0])" -ForegroundColor Green }
    else { Write-Host "FAIL  live reload: $($case[0])" -ForegroundColor Red; $failed++ }
}

$errors = Join-Path $env:LOCALAPPDATA "marleo\errors.log"
if (Test-Path $errors) { Write-Host "`nJavaScript errors logged in $errors" -ForegroundColor Yellow }

Write-Host ""
if ($failed) { Write-Host "$failed test(s) failed" -ForegroundColor Red; exit 1 }
Write-Host "All tests passed. Screenshot: $out\smoke.png" -ForegroundColor Green

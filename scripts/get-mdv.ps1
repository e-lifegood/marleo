<#
  One-line installer for mdv, published with every release as "install.ps1":

    irm https://github.com/e-lifegood/mdv/releases/latest/download/install.ps1 | iex

  Downloads the latest release to %LOCALAPPDATA%\Programs\mdv (no admin rights) and registers it with Explorer.
  If the .NET 10 Desktop Runtime is installed it uses the small build, otherwise the standalone one.
  Uninstall:  mdv --unregister ; Remove-Item -Recurse "$env:LOCALAPPDATA\Programs\mdv"
#>
& {
    $ErrorActionPreference = "Stop"
    $repo = "e-lifegood/mdv"
    $dest = Join-Path $env:LOCALAPPDATA "Programs\mdv"

    $desktop = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.WindowsDesktop.App"
    $hasRuntime = (Test-Path $desktop) -and (Get-ChildItem $desktop -Directory | Where-Object { $_.Name -like "10.*" })
    $asset = if ($hasRuntime) { "mdv-win-x64.zip" } else { "mdv-win-x64-standalone.zip" }
    $url = "https://github.com/$repo/releases/latest/download/$asset"

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("mdv-" + [guid]::NewGuid())
    New-Item -ItemType Directory $tmp | Out-Null
    try {
        Write-Host "Downloading $asset ..." -ForegroundColor Cyan
        $ProgressPreference = "SilentlyContinue"
        Invoke-WebRequest $url -OutFile (Join-Path $tmp $asset) -UseBasicParsing

        Get-Process mdv -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dest*" } | Stop-Process -Force
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        Expand-Archive (Join-Path $tmp $asset) -DestinationPath $dest -Force

        & (Join-Path $dest "mdv.exe") --register --quiet | Out-Null
        Write-Host "mdv installed to $dest" -ForegroundColor Green
        Write-Host "  - Right-click a folder -> Show more options -> Open with mdv"
        Write-Host "  - Right-click a .md file -> Open with -> mdv -> Always   (makes double-click open mdv)"
        Write-Host "  - Type 'mdv' in Explorer's address bar, or 'mdv <path>' in a new terminal"
    }
    finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

<#
  One-line installer for Marleo, published with every release as "install.ps1":

    irm https://github.com/e-lifegood/marleo/releases/latest/download/install.ps1 | iex

  Downloads the latest release to %LOCALAPPDATA%\Programs\marleo (no admin rights) and registers it with Explorer.
  If the .NET 10 Desktop Runtime is installed it uses the small build, otherwise the standalone one.
  Upgrading from mdv (the name up to v0.1.0) removes the old install; settings, window state and view
  preferences are carried over by Marleo itself.
  Uninstall:  marleo --unregister ; Remove-Item -Recurse "$env:LOCALAPPDATA\Programs\marleo"
#>
& {
    $ErrorActionPreference = "Stop"
    $repo = "e-lifegood/marleo"
    $dest = Join-Path $env:LOCALAPPDATA "Programs\marleo"
    $legacy = Join-Path $env:LOCALAPPDATA "Programs\mdv"

    $desktop = Join-Path $env:ProgramFiles "dotnet\shared\Microsoft.WindowsDesktop.App"
    $hasRuntime = (Test-Path $desktop) -and (Get-ChildItem $desktop -Directory | Where-Object { $_.Name -like "10.*" })
    $asset = if ($hasRuntime) { "marleo-win-x64.zip" } else { "marleo-win-x64-standalone.zip" }
    $url = "https://github.com/$repo/releases/latest/download/$asset"

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("marleo-" + [guid]::NewGuid())
    New-Item -ItemType Directory $tmp | Out-Null
    try {
        Write-Host "Downloading $asset ..." -ForegroundColor Cyan
        $ProgressPreference = "SilentlyContinue"
        Invoke-WebRequest $url -OutFile (Join-Path $tmp $asset) -UseBasicParsing

        # Upgrade from mdv (v0.1.0): unregister and remove the old install.
        if (Test-Path $legacy) {
            Write-Host "Removing the previous version (mdv) from $legacy ..." -ForegroundColor Cyan
            Get-Process mdv -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$legacy*" } | Stop-Process -Force
            $oldExe = Join-Path $legacy "mdv.exe"
            if (Test-Path $oldExe) { Start-Process $oldExe -ArgumentList "--unregister", "--quiet" -Wait }
            Remove-Item $legacy -Recurse -Force
            # Take the old folder off the user PATH (raw value, so %VARIABLES% in other entries stay unexpanded).
            $envKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $true)
            try {
                $userPath = $envKey.GetValue("Path", "", "DoNotExpandEnvironmentNames")
                $kept = @($userPath -split ";" | Where-Object {
                    $_ -and ([Environment]::ExpandEnvironmentVariables($_).TrimEnd("\") -ne $legacy.TrimEnd("\")) })
                if (($kept -join ";") -ne $userPath) { $envKey.SetValue("Path", ($kept -join ";"), "ExpandString") }
            }
            finally { $envKey.Close() }
        }

        Get-Process marleo -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$dest*" } | Stop-Process -Force
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        Expand-Archive (Join-Path $tmp $asset) -DestinationPath $dest -Force

        Start-Process (Join-Path $dest "marleo.exe") -ArgumentList "--register", "--quiet" -Wait
        Write-Host "Marleo installed to $dest" -ForegroundColor Green
        Write-Host "  - Right-click a folder -> Show more options -> Open with Marleo"
        Write-Host "  - Right-click a .md file -> Open with -> Marleo -> Always   (makes double-click open Marleo)"
        Write-Host "  - Type 'marleo' in Explorer's address bar, or 'marleo <path>' in a new terminal ('mdv' still works too)"
    }
    finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

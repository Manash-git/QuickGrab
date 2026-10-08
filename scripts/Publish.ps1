param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'CheckReleaseDocs.ps1')
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $includeRuntime = if ($SelfContained) { 'true' } else { 'false' }
    dotnet publish src/DownloadManager.App -c Release -r win-x64 --self-contained $includeRuntime -o artifacts/windows-x64/app
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    dotnet publish src/QuickGrab.NativeHost -c Release -r win-x64 --self-contained $includeRuntime -o artifacts/windows-x64/bridge
    if ($LASTEXITCODE -ne 0) { throw 'Native host publish failed.' }
    Copy-Item extension artifacts/windows-x64/extension -Recurse -Force
    Copy-Item RegisterBrowser.cmd,UnregisterBrowser.cmd artifacts/windows-x64 -Force
    Copy-Item docs artifacts/windows-x64/docs -Recurse -Force
    Copy-Item CHANGELOG.md,README.md,LICENSE artifacts/windows-x64 -Force
    Write-Host 'Keep app, bridge and extension together. Run RegisterBrowser.cmd from artifacts/windows-x64.'
} finally { Pop-Location }

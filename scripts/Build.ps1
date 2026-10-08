$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'CheckReleaseDocs.ps1')
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet build src/DownloadManager.App -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
} finally { Pop-Location }

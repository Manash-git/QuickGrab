$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    node tests/browser/handoff.test.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Browser handoff tests failed.' }
} finally { Pop-Location }

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$data = Get-Content (Join-Path $root 'docs/releases.json') -Raw | ConvertFrom-Json
if ($data.schema -ne 1) { throw 'Unsupported release documentation schema.' }
$records = @($data.releases)
if ($records.Count -eq 0 -or $records[0].version -ne '0.1.1') { throw 'History must begin at 0.1.1.' }
$previous = [version]'0.0.0'
foreach ($record in $records) {
    $current = [version]$record.version
    if ($current -le $previous) { throw 'Release versions must be unique and ascending.' }
    $previous = $current
    foreach ($field in @('title','date','migration')) {
        if ([string]::IsNullOrWhiteSpace([string]$record.$field)) { throw "Missing $field in $current." }
    }
    foreach ($field in @('new','fixed','validation','limitations')) {
        $items = @($record.$field)
        if ($items.Count -eq 0) { throw "Missing $field in $current." }
        foreach ($item in $items) {
            if ([string]::IsNullOrWhiteSpace([string]$item)) { throw "Empty $field in $current." }
        }
    }
    if (@($record.modified).Count -eq 0) { throw "Missing modifications in $current." }
    foreach ($change in $record.modified) {
        if ([string]::IsNullOrWhiteSpace([string]$change.change) -or [string]::IsNullOrWhiteSpace([string]$change.why)) {
            throw "Each modification needs a change and reason in $current."
        }
    }
}
if ($records[-1].version -ne $data.latest) { throw 'Latest record mismatch.' }
foreach ($project in @('src/DownloadManager.App/DownloadManager.App.csproj','src/QuickGrab.NativeHost/QuickGrab.NativeHost.csproj')) {
    [xml]$xml = Get-Content (Join-Path $root $project) -Raw
    $version = $xml.SelectSingleNode('//Version').InnerText
    if ($version -ne $data.latest) { throw "Version mismatch in $project." }
}
$extension = Get-Content (Join-Path $root 'extension/manifest.json') -Raw | ConvertFrom-Json
if ($extension.version -ne $data.latest) { throw 'Extension version mismatch.' }
foreach ($file in @('CHANGELOG.md','docs/PROJECT-HANDBOOK.md')) {
    $content = Get-Content (Join-Path $root $file) -Raw
    foreach ($record in $records) {
        if (-not $content.Contains($record.version)) { throw "Missing $($record.version) in $file." }
    }
}
Write-Host "Release documentation structure verified through v$($data.latest). Review accuracy and test evidence before delivery."

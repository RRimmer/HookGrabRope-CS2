$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'HookGrabRope/HookGrabRope.csproj'
dotnet build $project -m:1 -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$bin = Join-Path $PSScriptRoot 'HookGrabRope/bin/Release/net10.0'
$zip = Join-Path $PSScriptRoot 'HookGrabRope-CS2.zip'
$temp = Join-Path $PSScriptRoot ('HookGrabRope/obj/' + [Guid]::NewGuid() + '.zip')
try {
    $archive = [IO.Compression.ZipFile]::Open($temp, 'Create')
    try {
        foreach ($file in 'HookGrabRope.dll', 'HookGrabRope.deps.json') {
            $entry = "addons/counterstrikesharp/plugins/HookGrabRope/$file"
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $bin $file), $entry) | Out-Null
        }
        $contentRoot = Join-Path $PSScriptRoot 'Content-addonmanager'
        $particles = @(Get-ChildItem (Join-Path $contentRoot 'particles') -Filter *.vpcf -File -Recurse | ForEach-Object {
            $_.FullName.Substring($contentRoot.Length+1).Replace('\','/')
        })
        $bank = [IO.File]::ReadAllText((Join-Path $contentRoot 'soundevents/hgr.vsndevts'))
        $events = @([regex]::Matches($bank, '"(HGR\.[^"]+)"\s*=') | ForEach-Object { $_.Groups[1].Value })
        $index = @{ Particles=$particles; Events=$events } | ConvertTo-Json
        $indexStream = New-Object IO.StreamWriter($archive.CreateEntry('addons/counterstrikesharp/plugins/HookGrabRope/addon-resources.json').Open())
        try { $indexStream.Write($index) } finally { $indexStream.Dispose() }
        foreach ($doc in 'README.md','CHANGELOG.md','INSTALL.md') {
            $docPath = Join-Path $PSScriptRoot $doc
            if ([IO.File]::Exists($docPath)) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$docPath,$doc) | Out-Null
            }
        }
        foreach ($item in Get-ChildItem -File -Recurse (Join-Path $PSScriptRoot 'Content-addonmanager'),(Join-Path $PSScriptRoot 'HookGrabRope/lang'),(Join-Path $PSScriptRoot 'configs'),(Join-Path $PSScriptRoot 'gamedata')) {
            $entry = $item.FullName.Substring($PSScriptRoot.Length+1).Replace('\','/')
            if ($entry -in 'Content-addonmanager/panorama/layout/custom_game/hgr.xml','Content-addonmanager/panorama/styles/custom_game/hgr.css') { continue }
            if ($entry.StartsWith('HookGrabRope/lang/')) { $entry = 'addons/counterstrikesharp/plugins/HookGrabRope/' + $entry.Substring('HookGrabRope/'.Length) }
            if ($entry.StartsWith('configs/')) { $entry = "addons/counterstrikesharp/$entry" }
            if ($entry.StartsWith('gamedata/')) { $entry = "addons/counterstrikesharp/plugins/HookGrabRope/$entry" }
            if ($entry -eq 'Content-addonmanager/panorama/styles/custom_game/hgr_menu.css') {
                $css = [IO.File]::ReadAllText($item.FullName)
                $config = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'configs/plugins/HookGrabRope/hgr.jsonc'))
                $hexColors = [regex]::Matches($config,'#[0-9a-fA-F]{6}') | ForEach-Object { $_.Value.ToLowerInvariant() } | Sort-Object -Unique
                foreach ($hex in $hexColors) {
                    $class = '.window .option.hex-' + $hex.Substring(1)
                    if (!$css.Contains($class + '{')) { $css += [Environment]::NewLine + $class + '{border-color:' + $hex + ';}' }
                }
                $writer = New-Object IO.StreamWriter($archive.CreateEntry($entry).Open())
                try { $writer.Write($css) } finally { $writer.Dispose() }
                continue
            }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$item.FullName,$entry) | Out-Null
        }
    } finally {
        $archive.Dispose()
    }
    if ([IO.File]::Exists($zip)) {
        try { [IO.File]::Replace($temp, $zip, [System.Management.Automation.Language.NullString]::Value) }
        catch [IO.IOException] {
            $zip = Join-Path $PSScriptRoot ('HookGrabRope-CS2-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip')
            [IO.File]::Move($temp,$zip)
        }
    } else {
        [IO.File]::Move($temp,$zip)
    }
    Write-Host "OK $zip"
} finally {
    if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) }
}

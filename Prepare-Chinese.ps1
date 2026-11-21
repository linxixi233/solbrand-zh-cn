param([string]$GameDir = 'D:\SteamLibrary\steamapps\common\Solbrand Demo')
$ErrorActionPreference = 'Stop'
$patchRoot = $PSScriptRoot
$runtimeDir = Join-Path $patchRoot 'runtime'
$manifest = Get-Content -LiteralPath (Join-Path $patchRoot 'dist\manifest.json') -Raw | ConvertFrom-Json
$sourcePack = Join-Path $GameDir 'Solbrand.pck'
if (!(Test-Path -LiteralPath $sourcePack)) { throw "Game not found: $GameDir" }
if ((Get-Item -LiteralPath $sourcePack).Length -ne $manifest.sourcePackSize) { throw 'Game version mismatch. Supported Steam build: 26815196.' }
function Read-PackIndex([string]$FilePath) {
    $stream = [IO.File]::OpenRead($FilePath)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt32() -ne 0x43504447) { throw 'Invalid PCK signature.' }
        $version = $reader.ReadUInt32()
        if ($version -notin 3,4) { throw 'Unsupported PCK version.' }
        $stream.Position = 20
        if ($reader.ReadUInt32() -ne 2) { throw 'Unsupported PCK flags.' }
        $base = $reader.ReadUInt64()
        $directory = $reader.ReadUInt64()
        $stream.Position = $directory
        $count = $reader.ReadUInt32()
        $entries = [ordered]@{}
        for ($i=0; $i -lt $count; $i++) {
            $length = $reader.ReadUInt32()
            $name = [Text.Encoding]::UTF8.GetString($reader.ReadBytes($length)).TrimEnd([char]0)
            $entries[$name] = @{Offset=($reader.ReadUInt64()+$base); Size=$reader.ReadUInt64(); Hash=$reader.ReadBytes(16); Flags=$reader.ReadUInt32()}
        }
        return @{Base=$base; Entries=$entries}
    } finally { $reader.Dispose() }
}
$original = Read-PackIndex $sourcePack
$overlayPath = Join-Path $patchRoot 'dist\solbrand-zh-overlay.pck'
$overlay = Read-PackIndex $overlayPath
foreach ($check in @(@('project.binary',$manifest.sourceProjectMd5), @('.godot/imported/Main.ink-e0f455a283e8899f6dee802ddd078da4.res',$manifest.sourceStoryMd5))) {
    if (!$original.Entries.Contains($check[0])) { throw 'Expected resource missing. Game version mismatch.' }
    $entry = $original.Entries[$check[0]]
    $inputStream = [IO.File]::OpenRead($sourcePack)
    try { $inputStream.Position=$entry.Offset; $bytes=[byte[]]::new($entry.Size); $inputStream.ReadExactly($bytes,0,$bytes.Length); $hash=[Convert]::ToHexString([Security.Cryptography.MD5]::HashData($bytes)).ToLowerInvariant() } finally { $inputStream.Dispose() }
    if ($hash -ne $check[1]) { throw 'Source resource checksum mismatch. Rebuild required.' }
}
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
$targetPack = Join-Path $runtimeDir 'Solbrand.pck.building'
Write-Host 'Preparing independent Chinese game copy (about 1.8 GB)...'
Copy-Item -LiteralPath $sourcePack -Destination $targetPack -Force
$target = [IO.File]::Open($targetPack,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite)
$overlayStream = [IO.File]::OpenRead($overlayPath)
$writer = [IO.BinaryWriter]::new($target)
try {
    $target.Position=$target.Length
    foreach ($name in $overlay.Entries.Keys) {
        while (($target.Position % 32) -ne 0) { $writer.Write([byte]0) }
        $entry=$overlay.Entries[$name]
        $overlayStream.Position=$entry.Offset
        $bytes=[byte[]]::new($entry.Size)
        $overlayStream.ReadExactly($bytes,0,$bytes.Length)
        if ([Convert]::ToHexString([Security.Cryptography.MD5]::HashData($bytes)) -ne [Convert]::ToHexString($entry.Hash)) { throw 'Overlay checksum mismatch.' }
        $original.Entries[$name]=@{Offset=$target.Position;Size=$bytes.Length;Hash=$entry.Hash;Flags=$entry.Flags}
        $writer.Write($bytes)
    }
    $directory=$target.Position
    $writer.Write([uint32]$original.Entries.Count)
    foreach ($name in $original.Entries.Keys) {
        $entry=$original.Entries[$name]
        $nameBytes=[Text.Encoding]::UTF8.GetBytes($name)
        $padding=(4-($nameBytes.Length % 4)) % 4
        $writer.Write([uint32]($nameBytes.Length+$padding))
        $writer.Write($nameBytes)
        if ($padding) { $writer.Write([byte[]]::new($padding)) }
        $writer.Write([uint64]($entry.Offset-$original.Base))
        $writer.Write([uint64]$entry.Size)
        $writer.Write([byte[]]$entry.Hash)
        $writer.Write([uint32]$entry.Flags)
    }
    $target.Position=32
    $writer.Write([uint64]$directory)
} finally { $writer.Dispose(); $overlayStream.Dispose() }
Move-Item -LiteralPath $targetPack -Destination (Join-Path $runtimeDir 'Solbrand.pck') -Force
foreach ($filename in 'Solbrand.exe','fmod.dll','fmodstudio.dll','steam_api64.dll') {
    Copy-Item -LiteralPath (Join-Path $GameDir $filename) -Destination (Join-Path $runtimeDir $filename) -Force
}
foreach ($directoryName in 'Audio','data_Solbrand_windows_x86_64') {
    $localDirectory=Join-Path $runtimeDir $directoryName
    $originalDirectory=Join-Path $GameDir $directoryName
    if (!(Test-Path -LiteralPath $localDirectory)) { New-Item -ItemType Junction -Path $localDirectory -Target $originalDirectory | Out-Null }
}
Set-Content -LiteralPath (Join-Path $runtimeDir 'steam_appid.txt') -Value '4826310' -Encoding ascii
Set-Content -LiteralPath (Join-Path $runtimeDir 'zh-patch-version.txt') -Value $manifest.version -Encoding ascii
Write-Host 'Ready. Run Start-Chinese.cmd. Original installation remains unchanged.'

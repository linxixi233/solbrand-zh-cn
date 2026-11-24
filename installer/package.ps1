$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$application = Join-Path $taskRoot 'dist\Solbrand汉化助手-美化版.exe'
$portable = Join-Path $taskRoot 'Solbrand汉化助手-美化版.exe'
$guide = Join-Path $taskRoot '美化版-使用方法.txt'
Copy-Item -LiteralPath $application -Destination $portable -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$assistantVersion = (Get-Item -LiteralPath $application).VersionInfo.FileVersion -replace '\.0$',''
$destination = Join-Path $taskRoot ('dist\Solbrand汉化助手-' + $assistantVersion + '-美化版.zip')
$zipStream = [IO.File]::Open($destination, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
try {
    $archive = [IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$portable,[IO.Path]::GetFileName($portable),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$guide,[IO.Path]::GetFileName($guide),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $notice = Join-Path $taskRoot '非商业学习说明.txt'
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$notice,[IO.Path]::GetFileName($notice),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $taskRoot 'LICENSE'),'LICENSE.txt',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $taskRoot 'translations\LICENSE.txt'),'LICENSE-translations.txt',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $taskRoot 'NOTICE.md'),'NOTICE.md',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    } finally { $archive.Dispose() }
} finally { $zipStream.Dispose() }
Get-FileHash -LiteralPath $portable -Algorithm SHA256
Get-Item -LiteralPath $destination | Select-Object Name,Length

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$outputDirectory = Join-Path $taskRoot 'dist'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($requiredFile in @('manifest.json','solbrand-zh-overlay.pck')) {
    if (!(Test-Path -LiteralPath (Join-Path $outputDirectory $requiredFile))) {
        throw 'Missing build data. First run: node tools/build.mjs <original game directory or PCK>'
    }
}
$sources = @((Join-Path $PSScriptRoot 'PatchEngine.cs'), (Join-Path $PSScriptRoot 'UiControls.cs'), (Join-Path $PSScriptRoot 'Program.cs'), (Join-Path $PSScriptRoot 'AssemblyInfo.cs'))
$commonArgs = @('/nologo','/codepage:65001','/optimize+','/platform:anycpu',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',
    "/win32manifest:$PSScriptRoot\app.manifest",
    "/win32icon:$PSScriptRoot\solbrand.ico",
    "/resource:$PSScriptRoot\solbrand.ico,patch.icon",
    "/resource:$taskRoot\dist\manifest.json,patch.manifest",
    "/resource:$taskRoot\dist\solbrand-zh-overlay.pck,patch.overlay")
& $compiler @commonArgs '/target:winexe' "/out:$outputDirectory\Solbrand汉化助手-美化版.exe" @sources
if ($LASTEXITCODE -ne 0) { throw 'GUI build failed.' }
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'SelfTests.cs')) {
    & $compiler @commonArgs '/target:exe' '/main:SolbrandZh.SelfTests' "/out:$outputDirectory\SolbrandZh.Tests.exe" @sources (Join-Path $PSScriptRoot 'SelfTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
}
Get-Item -LiteralPath (Join-Path $outputDirectory 'Solbrand汉化助手-美化版.exe') | Select-Object FullName,Length

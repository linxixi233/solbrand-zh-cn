param(
    [string]$Overlay = '',
    [string]$LogName = 'lifecycle-fixed',
    [switch]$ExpectDuplicateError,
    [string]$ScriptName = 'lifecycle.gd',
    [string]$GodotExe = 'godot'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if (!$Overlay) { $Overlay = Join-Path $taskRoot 'dist\solbrand-zh-overlay.pck' }
$evidence = Join-Path $taskRoot 'analysis\hotfix-0.2.1'
New-Item -ItemType Directory -Force -Path (Join-Path $evidence 'profile') | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = (Get-Command $GodotExe -ErrorAction Stop).Source
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Environment['APPDATA'] = Join-Path $evidence 'profile'
foreach ($argument in @('--headless','--quit-after','90','--path',(Join-Path $taskRoot 'tools\validation'),'--script',"res://$ScriptName",'--',(Resolve-Path -LiteralPath $Overlay).Path)) {
    $start.ArgumentList.Add($argument)
}
$process = [Diagnostics.Process]::Start($start)
$stdoutTask = $process.StandardOutput.ReadToEndAsync()
$stderrTask = $process.StandardError.ReadToEndAsync()
if (!$process.WaitForExit(30000)) {
    $process.Kill()
    $process.WaitForExit()
}
$stdout = $stdoutTask.GetAwaiter().GetResult()
$stderr = $stderrTask.GetAwaiter().GetResult()
Set-Content -LiteralPath (Join-Path $evidence "$LogName.stdout.log") -Value $stdout -Encoding utf8
Set-Content -LiteralPath (Join-Path $evidence "$LogName.stderr.log") -Value $stderr -Encoding utf8
if ($ExpectDuplicateError) {
    $duplicateErrors = [regex]::Matches($stderr, "Signal 'ready' is already connected").Count
    if (!$duplicateErrors) { throw 'Original duplicate-ready bug was not reproduced.' }
    "REPRODUCED: $duplicateErrors duplicate-ready errors in original patch."
    return
}
$stdout
$stderr
if ($process.ExitCode -ne 0 -or $stderr -match '(?m)^(ERROR|SCRIPT ERROR):' -or $stdout -notmatch 'PASS:') {
    throw "Godot lifecycle regression failed; see analysis/hotfix-0.2.1/$LogName.*.log"
}

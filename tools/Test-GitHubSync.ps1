$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $workspace
$source = Join-Path $workspace 'tools\Sync-GitHub.ps1'
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw 'Uploader contains PowerShell parse errors.' }
$definitions = $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] },$false)
foreach ($definition in $definitions) { . ([scriptblock]::Create($definition.Extent.Text)) }
$taskRoot = Join-Path $workspace ('analysis\github-sync-tests\' + [Guid]::NewGuid().ToString('N'))
$bare = $taskRoot + '-remote.git'
New-Item -ItemType Directory -Force -Path $taskRoot | Out-Null
Set-Location -LiteralPath $taskRoot
$settings = @{owner='sync-tests'}
$sourcePaths = @('.gitignore','README.md','tools')
$passed = 0
function Assert($condition,$message) { if (!$condition) { throw $message } }
function Pass($title) { $script:passed++; Write-Host ('PASS ' + $title) }
function Must-Reject([scriptblock]$action,$message) {
    $rejected = $false
    try { & $action } catch { $rejected = $true }
    Assert $rejected $message
}
Pass 'Windows PowerShell 5.1 parser'
$fakeGit = Join-Path $taskRoot 'FakeGitCredential.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /out:$fakeGit (Join-Path $workspace 'tools\tests\FakeGitCredential.cs')
if ($LASTEXITCODE -ne 0) { throw 'Credential protocol fixture compilation failed.' }
foreach ($withBom in @($true,$false)) {
    $previousEncoding = [Console]::InputEncoding
    try {
        [Console]::InputEncoding = New-Object Text.UTF8Encoding($withBom)
        $credential = Get-GitHubCredential -GitExecutable $fakeGit
        Assert ($credential -and $credential.username -eq 'fixture' -and $credential.password -eq 'fixture-not-a-real-token') 'Git credential wire protocol was corrupted by the console encoding.'
        Assert (([Console]::InputEncoding.GetPreamble().Length -gt 0) -eq $withBom) 'Credential lookup changed the callers input encoding.'
        $credential = $null
    } finally { [Console]::InputEncoding = $previousEncoding }
    Pass ('credential protocol without BOM under UTF-8 BOM console=' + $withBom)
}
Run-Git -GitArgs @('init','--quiet')
Run-Git -GitArgs @('init','--bare','--quiet',$bare)
New-Item -ItemType Directory -Force -Path 'tools','dist' | Out-Null
[IO.File]::WriteAllText((Join-Path $taskRoot '.gitignore'),"dist/`n.github-upload.local.json`n")
[IO.File]::WriteAllText((Join-Path $taskRoot 'README.md'),'fixture readme')
[IO.File]::WriteAllText((Join-Path $taskRoot 'tools\fixture.ps1'),'Write-Host fixture')
[IO.File]::WriteAllText((Join-Path $taskRoot 'dist\game.pck'),'PRIVATE FIXTURE')
[IO.File]::WriteAllText((Join-Path $taskRoot '.github-upload.local.json'),'{"owner":"fixture"}')
$files = @(Get-PublishFiles)
Assert ($files.Count -eq 3) 'Ignored game or local configuration leaked into the upload list.'
Check-PublishFiles $files
Pass 'source allowlist excludes generated game data and local configuration'
$before = Get-SourceHash
Assert ($before -eq (Get-SourceHash)) 'Fingerprint is unstable.'
[IO.File]::AppendAllText((Join-Path $taskRoot 'README.md'),' changed')
Assert ($before -ne (Get-SourceHash)) 'Changed source was not detected.'
Pass 'source fingerprints are stable and detect edits'
Run-Git -GitArgs @('add','--all','--','.gitignore','README.md','tools')
Run-Git -GitArgs @('-c','user.name=Sync Tests','-c','user.email=sync-tests@example.invalid','commit','--quiet','-m','fixture')
Run-Git -GitArgs @('remote','add','origin',$bare)
Assert ((& git remote get-url origin).Trim() -eq $bare) 'Git argument forwarding lost remote arguments.'
Pass 'commit and remote argument forwarding'
Push-Ref -PushArgs @('push','--quiet','--set-upstream','origin','HEAD:refs/heads/main')
$localHead = (& git rev-parse HEAD).Trim()
$remoteHead = (& git --git-dir $bare rev-parse refs/heads/main).Trim()
Assert ($localHead -eq $remoteHead) 'Source push did not reach the local fixture remote.'
Pass 'push updates the intended branch on an isolated local remote'
Remove-Item -LiteralPath (Join-Path $taskRoot 'README.md')
Run-Git -GitArgs @('add','--all','--','README.md')
$removed = @(& git -c core.quotepath=false diff --cached --name-status)
Assert (($removed -join "`n") -match 'D\s+README.md') 'Source deletion was not staged.'
Pass 'tracked file deletions can be uploaded'
Must-Reject { Check-PublishFiles @('dist/game.pck') } 'Game binary upload was accepted.'
Pass 'game binary rejection'
$fake = 'ghp_' + ('x' * 36)
[IO.File]::WriteAllText((Join-Path $taskRoot 'tools\bad.txt'),$fake)
Must-Reject { Check-PublishFiles @('tools/bad.txt') } 'A token-shaped value was accepted.'
Pass 'token-shaped source rejection without printing its value'
$script:githubHeaders = @{}
Must-Reject { Invoke-GitHub 'https://example.invalid/user' } 'Credential forwarding to a non-GitHub host was accepted.'
Pass 'API credentials are restricted to GitHub hosts'
$notesPath = Join-Path $taskRoot 'notes.md'
$notesText = 'Release notes: 中文说明与原始文本。'
[IO.File]::WriteAllText($notesPath,$notesText,[Text.Encoding]::UTF8)
$extendedNotes = Get-Content -LiteralPath $notesPath -Raw -Encoding UTF8
$payloadJson = ConvertTo-GitHubJson @{body=$extendedNotes;draft=$true;labels=@('test');empty=@()}
$payload = $payloadJson | ConvertFrom-Json
Assert ($payload.body -is [string] -and $payload.body -eq $notesText) 'File metadata changed the release body from text into an object.'
Assert ($payloadJson -notmatch 'PSPath|PSDrive|PSProvider') 'Filesystem metadata leaked into the release payload.'
Assert ($payload.draft -eq $true -and $payload.labels.Count -eq 1 -and $payload.empty.Count -eq 0) 'Payload normalization changed JSON values.'
Pass 'release notes serialize as plain text without filesystem metadata'
function Invoke-GitHub {
    param([string]$Path,[string]$Method='Get',$Body=$null,[switch]$NotFoundOK,[string]$UploadFile='')
    if ($Path -match '/releases/tags/') { return $null }
    if ($Path -match '/releases\?') { return @([pscustomobject]@{id=40;tag_name='other';draft=$false},[pscustomobject]@{id=42;tag_name='v9.9.9-r2';draft=$true}) }
    throw 'An offline release test attempted an unexpected API call.'
}
$draft = Get-GitHubRelease -FullName 'fixture/repo' -Tag 'v9.9.9-r2'
Assert ($draft.id -eq 42 -and $draft.draft) 'An existing draft was not found during resume.'
Pass 'draft releases are found through the owner release list'
Run-Git -GitArgs @('tag','v9.9.9',$localHead)
$script:mockReleases = @{}
$script:mockTags = @{'v9.9.9'=$localHead}
function Get-GitHubRelease { param([string]$FullName,[string]$Tag); return $script:mockReleases[$Tag] }
function Get-GitHubTagCommit { param([string]$FullName,[string]$Tag); return $script:mockTags[$Tag] }
$newCommit = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
$target = Select-GitHubReleaseTarget -FullName 'fixture/repo' -BaseTag 'v9.9.9' -Commit $newCommit
Assert ($target.Tag -eq 'v9.9.9-r2') 'An old tag without a release was incorrectly reused for a new commit.'
Assert ((& git rev-list -n 1 refs/tags/v9.9.9).Trim() -eq $localHead) 'An existing tag was moved.'
Pass 'an existing tag without a release is preserved and the next revision is selected'
$script:mockTags['v9.9.9-r2']=$newCommit
$script:mockReleases['v9.9.9-r2']=[pscustomobject]@{id=42;draft=$true;target_commitish=$newCommit}
$target = Select-GitHubReleaseTarget -FullName 'fixture/repo' -BaseTag 'v9.9.9' -Commit $newCommit
Assert ($target.Tag -eq 'v9.9.9-r2' -and $target.Release.id -eq 42) 'A matching draft was not resumed.'
Pass 'matching drafts and remote tags are reused without duplicates'
$target = Select-GitHubReleaseTarget -FullName 'fixture/repo' -BaseTag 'v9.9.9' -Commit $newCommit -Force $true
Assert ($target.Tag -eq 'v9.9.9-r3') 'Forced publication did not select an unused revision.'
Pass 'forced publication chooses an unused revision'
Set-Location -LiteralPath $workspace
Write-Host ('ALL PASSED: ' + $passed)

param(
    [string]$RepositoryName = 'solbrand-zh-cn',
    [string]$GameDirectory = '',
    [switch]$CheckOnly,
    [switch]$CheckLogin,
    [switch]$SkipRelease,
    [switch]$ForceRelease
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
$settingsPath = Join-Path $taskRoot '.github-upload.local.json'
$settings = @{}
if (Test-Path -LiteralPath $settingsPath) {
    $saved = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($property in $saved.PSObject.Properties) { $settings[$property.Name] = $property.Value }
}
$allowedPaths = @('.gitignore','README.md','LICENSE','NOTICE.md','CONTRIBUTING.md','CHANGELOG.md',
    'Prepare-Chinese.ps1','Start-Chinese.cmd','美化版-使用方法.txt','非商业学习说明.txt','术语表.txt',
    '一键上传到GitHub.cmd','一键上传说明.txt','installer','patch','translations','tools','docs')
$sourcePaths = @($allowedPaths | Where-Object { Test-Path -LiteralPath (Join-Path $taskRoot $_) })

function Save-Settings {
    $settings | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
}
function Run-Git {
    param([string[]]$GitArgs)
    $ErrorActionPreference = 'Continue'
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) { throw ('Git 操作失败：' + ($GitArgs -join ' ')) }
}
function Get-PublishFiles {
    $listed = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard -- $sourcePaths)
    if ($LASTEXITCODE -ne 0) { throw '无法读取项目文件列表。' }
    return @($listed | Sort-Object -Unique | Where-Object { Test-Path -LiteralPath (Join-Path $taskRoot $_) -PathType Leaf })
}
function Get-SourceHash {
    $rows = foreach ($file in Get-PublishFiles) {
        $digest = (Get-FileHash -LiteralPath (Join-Path $taskRoot $file) -Algorithm SHA256).Hash
        $file.Replace('\','/') + ':' + $digest
    }
    $hasher = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes(($rows -join "`n"))))).Replace('-','').ToLowerInvariant() }
    finally { $hasher.Dispose() }
}
function Check-PublishFiles {
    param([string[]]$Files)
    $secretPattern = '\bgh[pousr]_[A-Za-z0-9_]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    foreach ($file in $Files) {
        if ($file -match '(^|/)(analysis|runtime|dist|\.git|\.codex|\.agents)(/|$)|\.(exe|dll|pck|zip|log)$|Solbrand-ZH\.path\.txt|\.github-upload\.local\.json|(^|/)\.env($|\.)') {
            throw ('上传列表含有不应公开的文件：' + $file)
        }
        if ($file -notmatch '\.(png|ico)$') {
            $content = [IO.File]::ReadAllText((Join-Path $taskRoot $file),[Text.Encoding]::UTF8)
            if ($content -match $secretPattern) { throw ('发现可能的令牌或私钥，请检查后重试：' + $file) }
        }
    }
}
function Find-Node {
    $command = Get-Command node -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $standard = Join-Path $env:ProgramFiles 'nodejs\node.exe'
    if (Test-Path -LiteralPath $standard) { return $standard }
    $bundled = @(Get-ChildItem -Path (Join-Path $env:ProgramFiles 'WindowsApps\OpenAI.CodexPrimaryRuntime.*\dependencies\node\bin\node.exe') -File -ErrorAction SilentlyContinue | Sort-Object FullName -Descending)
    if ($bundled.Count) { return $bundled[0].FullName }
    throw '未找到 Node.js。请安装 Node.js 24 或更新版本，或使用 -SkipRelease 仅上传源码。'
}
function Find-OriginalPack {
    $choices = @()
    if ($GameDirectory) { $choices += $GameDirectory }
    if ($settings.ContainsKey('gameDirectory')) { $choices += $settings.gameDirectory }
    $gameConfig = Join-Path $taskRoot 'Solbrand-ZH.path.txt'
    if (Test-Path -LiteralPath $gameConfig) { $choices += (Get-Content -LiteralPath $gameConfig -Raw -Encoding UTF8).Trim() }
    $choices += 'D:\SteamLibrary\steamapps\common\Solbrand Demo'
    foreach ($choice in $choices) {
        if (!$choice) { continue }
        if (Test-Path -LiteralPath $choice -PathType Leaf) { return [IO.Path]::GetFullPath($choice) }
        $buildProfiles = [IO.File]::ReadAllText((Join-Path $taskRoot 'translations\builds.json')) | ConvertFrom-Json
        $sizes = @($buildProfiles.PSObject.Properties | ForEach-Object { [long]$_.Value.sourcePackSize })
        foreach ($relative in @('Solbrand.pck','.solbrand-zh\Solbrand.pck.original')) {
            $candidate = Join-Path $choice $relative
            if ((Test-Path -LiteralPath $candidate -PathType Leaf) -and $sizes -contains (Get-Item -LiteralPath $candidate).Length) {
                $settings.gameDirectory = [IO.Path]::GetFullPath($choice)
                return [IO.Path]::GetFullPath($candidate)
            }
        }
    }
    throw '未找到原版游戏资源。请先安装匹配版本的 Demo；也可通过 -GameDirectory 指定游戏目录，或用 -SkipRelease 仅上传源码。'
}
function Get-GitHubCredential {
    param([string]$GitExecutable = '')
    # Use the standard Git credential protocol. Never print or save the returned token.
    $script:lastCredentialError = ''
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = if ($GitExecutable) { $GitExecutable } else { (Get-Command git -ErrorAction Stop).Source }
    $start.Arguments = '-c credential.interactive=never credential fill'
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $start.EnvironmentVariables['GCM_INTERACTIVE'] = 'never'
    $start.EnvironmentVariables['GCM_GUI_PROMPT'] = 'false'
    # .NET Framework copies Console.InputEncoding when creating the redirected
    # StreamWriter. UTF-8 with a BOM corrupts the first "protocol=https" field.
    $previousInputEncoding = [Console]::InputEncoding
    try {
        [Console]::InputEncoding = New-Object Text.UTF8Encoding($false)
        $process = [Diagnostics.Process]::Start($start)
    } finally { [Console]::InputEncoding = $previousInputEncoding }
    $process.StandardInput.WriteLine('protocol=https')
    $process.StandardInput.WriteLine('host=github.com')
    if ($settings.ContainsKey('owner')) { $process.StandardInput.WriteLine('username=' + $settings.owner) }
    $process.StandardInput.WriteLine(); $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(20000)) { $process.Kill(); $process.Dispose(); $script:lastCredentialError='Git 凭据查询超时'; return $null }
    $text = $stdout.GetAwaiter().GetResult(); $errorText = $stderr.GetAwaiter().GetResult()
    $exitCode = $process.ExitCode; $process.Dispose()
    if ($exitCode -ne 0) {
        $safeError = $errorText -replace '\bgh[pousr]_[A-Za-z0-9_]+|\bgithub_pat_[A-Za-z0-9_]+','[redacted]'
        $safeError = $safeError -replace '(?i)(password|token|authorization)(\s*[:=]\s*)[^\r\n]+','$1$2[redacted]'
        $script:lastCredentialError = ('Git 退出码 ' + $exitCode + '：' + $safeError.Trim())
        return $null
    }
    $text = $text.TrimStart([char]0xFEFF)
    $values = @{}
    foreach ($line in ($text -split "`n")) {
        $separator = $line.IndexOf('=')
        if ($separator -gt 0) { $values[$line.Substring(0,$separator)] = $line.Substring($separator+1).TrimEnd("`r") }
    }
    if ($values.ContainsKey('password') -and $values.password) { return $values }
    $script:lastCredentialError = 'Git 凭据查询成功，但没有返回可用的凭据'
    return $null
}
function ConvertTo-GitHubJson {
    param($Body)
    function Clean-JsonValue($Value) {
        if ($null -eq $Value) { return $null }
        if ($Value -is [string]) {
            # Windows PowerShell adds PSPath/PSDrive metadata to Get-Content
            # strings. Clone the text so JSON contains only its actual value.
            return [Text.Encoding]::UTF8.GetString([Text.Encoding]::UTF8.GetBytes([string]$Value))
        }
        if ($Value -is [bool] -or $Value -is [int] -or $Value -is [long] -or $Value -is [double]) { return $Value }
        if ($Value -is [Collections.IDictionary]) {
            $clean = @{}
            foreach ($key in $Value.Keys) { $clean[[string]$key] = Clean-JsonValue $Value[$key] }
            return $clean
        }
        if ($Value -is [Collections.IEnumerable]) {
            $items = @(foreach ($item in $Value) { Clean-JsonValue $item })
            return ,$items
        }
        throw ('GitHub 请求中包含不支持的数据类型：' + $Value.GetType().FullName)
    }
    $cleanBody = Clean-JsonValue $Body
    return ConvertTo-Json -InputObject $cleanBody -Depth 8 -Compress
}
function Get-GitHubRelease {
    param([string]$FullName,[string]$Tag)
    $release = Invoke-GitHub ('/repos/' + $FullName + '/releases/tags/' + $Tag) -NotFoundOK
    if ($release) { return $release }
    # Listing includes the owner's drafts; tag lookup may only find published releases.
    for ($page = 1; $page -le 100; $page++) {
        $batchResult = Invoke-GitHub ('/repos/' + $FullName + '/releases?per_page=100&page=' + $page)
        $batch = @($batchResult | Where-Object { $_ -and $_.id })
        $match = @($batch | Where-Object { $_.tag_name -eq $Tag })
        if ($match.Count) { return $match[0] }
        if ($batch.Count -lt 100) { return $null }
    }
    throw 'Release 列表过长，无法安全确认现有版本。'
}
function Get-GitHubTagCommit {
    param([string]$FullName,[string]$Tag)
    $reference = Invoke-GitHub ('/repos/' + $FullName + '/git/ref/tags/' + $Tag) -NotFoundOK
    if (!$reference) { return $null }
    $target = $reference.object
    for ($depth = 0; $depth -lt 8; $depth++) {
        if ($target.type -eq 'commit') { return [string]$target.sha }
        if ($target.type -ne 'tag') { throw ('无法解析标签：' + $Tag) }
        $tagObject = Invoke-GitHub ('/repos/' + $FullName + '/git/tags/' + $target.sha)
        $target = $tagObject.object
    }
    throw ('标签层级过深：' + $Tag)
}
function Select-GitHubReleaseTarget {
    param([string]$FullName,[string]$BaseTag,[string]$Commit,[bool]$Force = $false)
    for ($revision = 1; $revision -le 1000; $revision++) {
        $tag = if ($revision -eq 1) { $BaseTag } else { $BaseTag + '-r' + $revision }
        $release = Get-GitHubRelease -FullName $FullName -Tag $tag
        $remoteCommit = Get-GitHubTagCommit -FullName $FullName -Tag $tag
        $localCommit = $null
        if (@(& git tag --list $tag) -contains $tag) { $localCommit = (& git rev-list -n 1 ('refs/tags/' + $tag)).Trim() }
        if (($localCommit -and $localCommit -ne $Commit) -or ($remoteCommit -and $remoteCommit -ne $Commit) -or ($Force -and $release)) { continue }
        return [pscustomobject]@{Tag=$tag;Revision=$revision;Release=$release;RemoteCommit=$remoteCommit;LocalCommit=$localCommit}
    }
    throw '未找到可用的 Release 标签。'
}
function Invoke-GitHub {
    param([string]$Path,[string]$Method = 'Get',$Body = $null,[switch]$NotFoundOK,[string]$UploadFile = '')
    $address = if ($Path -match '^https://') { [Uri]$Path } else { [Uri]('https://api.github.com' + $Path) }
    if ($address.Host -notin @('api.github.com','uploads.github.com')) { throw '拒绝向非 GitHub API 地址发送凭据。' }
    $parameters = @{ Uri=$address; Method=$Method; Headers=$script:githubHeaders; TimeoutSec=90; ErrorAction='Stop' }
    if ($UploadFile) { $parameters.InFile=$UploadFile; $parameters.ContentType='application/octet-stream' }
    elseif ($null -ne $Body) { $parameters.Body=[Text.Encoding]::UTF8.GetBytes((ConvertTo-GitHubJson $Body)); $parameters.ContentType='application/json; charset=utf-8' }
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try { return Invoke-RestMethod @parameters }
        catch {
            $statusCode = 0
            if ($_.Exception.Response) { try { $statusCode = [int]$_.Exception.Response.StatusCode } catch { } }
            if ($NotFoundOK -and $statusCode -eq 404) { return $null }
            # Only retry reads here. Mutations are reconciled by their callers.
            if ($Method -eq 'Get' -and $attempt -lt 3 -and ($statusCode -eq 0 -or $statusCode -ge 500)) { Start-Sleep -Seconds 2; continue }
            throw ('GitHub 请求失败：' + $Method + ' ' + $address.AbsolutePath + '，HTTP ' + $statusCode + '。请检查网络、账号权限或稍后重试。')
        }
    }
}
function Push-Ref {
    param([string[]]$PushArgs)
    $ErrorActionPreference = 'Continue'
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $transferOutput = @(& git @script:gitNetworkOptions -c ('credential.username=' + $settings.owner) @PushArgs 2>&1)
        if ($LASTEXITCODE -eq 0) {
            $transferOutput | ForEach-Object { Write-Host ([string]$_) }
            if ($attempt -gt 1) { Write-Host ('第 ' + $attempt + ' 次连接重试成功。') -ForegroundColor Green }
            return
        }
        if ($attempt -lt 3) { Write-Host ('Git 连接未完成，2 秒后重试（' + $attempt + '/3）…') -ForegroundColor Yellow; Start-Sleep -Seconds 2 }
    }
    $transferOutput | ForEach-Object { Write-Host ([string]$_) }
    throw 'Git 推送未完成。请检查网络；如远端有其他改动，请先拉取并处理冲突后重试。脚本不会强制覆盖远端。'
}

try {
    Write-Host 'Solbrand 一键 GitHub 上传工具' -ForegroundColor Cyan
    Write-Host '首次创建公开仓库和 Release；后续自动提交并上传改动。'
    if (!(Get-Command git -ErrorAction SilentlyContinue)) { throw '请先安装 Git for Windows（包含 Git Credential Manager）。' }
    if (!(Test-Path -LiteralPath (Join-Path $taskRoot '.git'))) {
        if ($CheckOnly) { throw '尚未初始化 Git 仓库。正常运行时会自动初始化。' }
        Run-Git -GitArgs @('init')
    }
    $gitRoot = (& git rev-parse --show-toplevel).Trim()
    if ([IO.Path]::GetFullPath($gitRoot) -ne [IO.Path]::GetFullPath($taskRoot)) { throw '项目目录与 Git 根目录不一致，已停止。' }
    # Include deleted tracked paths so one-click sync also uploads removals.
    $trackedBefore = @(& git -c core.quotepath=false ls-files)
    $sourcePaths = @($allowedPaths | Where-Object {
        $candidate = $_
        (Test-Path -LiteralPath (Join-Path $taskRoot $candidate)) -or
            @($trackedBefore | Where-Object { $_ -eq $candidate -or $_.StartsWith($candidate + '/') }).Count -gt 0
    })
    $files = @(Get-PublishFiles)
    Check-PublishFiles $files
    if ($CheckOnly) {
        Write-Host ('源码检查通过：' + $files.Count + ' 个文件。') -ForegroundColor Green
        if (!$SkipRelease) { Write-Host ('Node.js：' + (Find-Node)); Write-Host ('原版资源：' + (Find-OriginalPack)) }
        Write-Host '本次仅检查，未登录、提交、上传或修改游戏。'
        exit 0
    }
    $sourceHash = Get-SourceHash
    if (!$CheckLogin -and !$ForceRelease -and $settings.ContainsKey('lastSourceHash') -and $settings.lastSourceHash -eq $sourceHash -and ($SkipRelease -or $settings.lastReleaseSourceHash -eq $sourceHash)) {
        Write-Host '没有新的源码改动，无需重复上传。' -ForegroundColor Green
        if ($settings.ContainsKey('repositoryUrl')) { Write-Host $settings.repositoryUrl }
        exit 0
    }
    $proxy = [Net.WebRequest]::DefaultWebProxy
    $githubUri = [Uri]'https://github.com'
    $script:gitNetworkOptions = @('-c','http.version=HTTP/1.1','-c','http.sslBackend=schannel','-c','http.sslVerify=true')
    if ($proxy -and !$proxy.IsBypassed($githubUri)) {
        $proxyUri = $proxy.GetProxy($githubUri)
        $env:HTTPS_PROXY = $proxyUri.AbsoluteUri; $env:HTTP_PROXY = $proxyUri.AbsoluteUri
        $script:gitNetworkOptions += @('-c',('http.proxy=' + $proxyUri.AbsoluteUri))
    }
    Write-Host '[1/6] 检查 GitHub 登录…' -ForegroundColor Cyan
    $credential = Get-GitHubCredential
    if (!$credential) {
        if ($CheckLogin) { throw ('无法读取现有登录：' + $script:lastCredentialError) }
        Write-Host '首次使用需要登录一次。Git 凭据管理器会显示设备码，请按提示在浏览器中授权。' -ForegroundColor Yellow
        Run-Git -GitArgs @('credential-manager','github','login','--device','--no-ui')
        $credential = Get-GitHubCredential
        if (!$credential) { throw ('未能读取 GitHub 登录状态：' + $script:lastCredentialError) }
    }
    $script:githubHeaders = @{ Authorization=('Bearer ' + $credential.password); Accept='application/vnd.github+json'; 'User-Agent'='Solbrand-GitHub-Sync'; 'X-GitHub-Api-Version'='2022-11-28' }
    $account = Invoke-GitHub '/user'
    $owner = [string]$account.login
    if ($settings.ContainsKey('owner') -and $settings.owner -ne $owner) { throw '当前 GitHub 账号与首次绑定账号不一致，请切回原账号再运行。' }
    Write-Host ('已登录 GitHub：' + $owner) -ForegroundColor Green
    if ($CheckLogin) {
        Write-Host '现有授权校验成功，未创建仓库、构建或上传文件。'
        exit 0
    }
    if (!$SkipRelease) {
        Write-Host '[2/6] 构建汉化助手与发布包…' -ForegroundColor Cyan
        $app = Join-Path $taskRoot 'dist\Solbrand汉化助手-美化版.exe'
        $zipPackage = Join-Path $taskRoot 'dist\Solbrand汉化助手-1.4-美化版.zip'
        $reuseBuild = !$ForceRelease -and $settings.ContainsKey('lastBuildSourceHash') -and $settings.lastBuildSourceHash -eq $sourceHash -and (Test-Path -LiteralPath $app) -and (Test-Path -LiteralPath $zipPackage)
        if ($reuseBuild) {
            $reuseBuild = (Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash -eq $settings.lastBuildExeHash -and (Get-FileHash -LiteralPath $zipPackage -Algorithm SHA256).Hash -eq $settings.lastBuildZipHash
        }
        if (!$reuseBuild) {
            $node = Find-Node; $originalPack = Find-OriginalPack
            $buildOutput = @(& $node tools/build-supported.mjs $originalPack)
            if ($LASTEXITCODE -ne 0) { $buildOutput | ForEach-Object { Write-Host $_ }; throw '补丁构建失败；未上传旧的安装包。' }
            $buildManifest = [IO.File]::ReadAllText((Join-Path $taskRoot 'dist\manifest.json'),[Text.Encoding]::UTF8) | ConvertFrom-Json
            Write-Host ('补丁构建完成：v' + $buildManifest.version + '，场景 ' + $buildManifest.localizedScenes + '，匹配词条 ' + $buildManifest.uiMessages + '。')
            & (Join-Path $taskRoot 'installer\build.ps1') | Out-Host
            $testOutput = @(& (Join-Path $taskRoot 'dist\SolbrandZh.Tests.exe') 2>&1)
            $testExit = $LASTEXITCODE; $testOutput | ForEach-Object { Write-Host $_ }
            if ($testExit -ne 0) { throw '测试未通过，已停止发布。' }
            & (Join-Path $taskRoot 'installer\package.ps1') | Out-Host
        } else { Write-Host '已找到与当前源码一致且校验通过的构建，继续上次上传。' }
        $assistantVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion -replace '\.0$',''
        $manifest = Get-Content -LiteralPath 'dist\manifest.json' -Raw -Encoding UTF8 | ConvertFrom-Json
        $patchVersion = [string]$manifest.version
        $releaseDirectory = Join-Path $taskRoot 'dist\release'
        New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
        $exeAsset = Join-Path $releaseDirectory ('Solbrand-ZH-Assistant-v' + $assistantVersion + '.exe')
        $zipAsset = Join-Path $releaseDirectory ('Solbrand-ZH-Assistant-v' + $assistantVersion + '-win.zip')
        Copy-Item -LiteralPath $app -Destination $exeAsset -Force
        Copy-Item -LiteralPath $zipPackage -Destination $zipAsset -Force
        $assets = @($exeAsset,$zipAsset)
        $hashes = foreach ($asset in $assets) { (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($asset) }
        $checksumFile = Join-Path $releaseDirectory 'SHA256SUMS.txt'
        $hashes | Set-Content -LiteralPath $checksumFile -Encoding ASCII
        $assets += $checksumFile
        $verificationPath = Join-Path $taskRoot 'installer\verification.json'
        if (Test-Path -LiteralPath $verificationPath) {
            $verification = Get-Content -LiteralPath $verificationPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $verification.installerVersion=$assistantVersion; $verification.patchVersion=$patchVersion
            $verification.exeSha256=(Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash.ToLowerInvariant()
            $verification | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $verificationPath -Encoding UTF8
        }
        $settings.lastBuildSourceHash=Get-SourceHash
        $settings.lastBuildExeHash=(Get-FileHash -LiteralPath $app -Algorithm SHA256).Hash
        $settings.lastBuildZipHash=(Get-FileHash -LiteralPath $zipPackage -Algorithm SHA256).Hash
        Save-Settings
    }
    if ($settings.ContainsKey('repositoryName')) { $RepositoryName = $settings.repositoryName }
    if ($RepositoryName -notmatch '^[A-Za-z0-9_.-]+$') { throw '仓库名只能包含字母、数字、点、横线和下划线。' }
    $fullName = $owner + '/' + $RepositoryName
    Write-Host ('发布账号与仓库：' + $fullName)
    Write-Host '[3/6] 准备公开仓库…' -ForegroundColor Cyan
    $remoteNames = @(& git remote)
    $remote = if ($remoteNames -contains 'origin') { @(& git remote get-url origin) } else { @() }
    $remoteUrl = ($remote -join '').Trim()
    $expectedUrl = 'https://github.com/' + $fullName + '.git'
    if ($remoteUrl -and $remoteUrl -ne $expectedUrl -and $remoteUrl -ne $expectedUrl.Replace('https://github.com/','git@github.com:')) { throw 'origin 已指向其他仓库，未修改或上传。' }
    $repository = Invoke-GitHub ('/repos/' + $fullName) -NotFoundOK
    if ($repository -and !$remoteUrl -and !$settings.ContainsKey('createdHere')) { throw ('同名仓库已存在，未覆盖。请在脚本参数中换一个仓库名：' + $fullName) }
    if (!$repository) {
        try { $repository = Invoke-GitHub '/user/repos' -Method Post -Body @{name=$RepositoryName;description='Solbrand Demo 简体中文汉化补丁与一键安装/回退助手 · 非商业学习交流';private=$false;auto_init=$false} }
        catch {
            $repository = Invoke-GitHub ('/repos/' + $fullName) -NotFoundOK
            if (!$repository -or $repository.description -ne 'Solbrand Demo 简体中文汉化补丁与一键安装/回退助手 · 非商业学习交流') { throw }
        }
        $settings.createdHere=$true
    }
    if ($repository.private) { throw '目标仓库是私有仓库，脚本未更改其可见性。请确认仓库设置。' }
    $settings.owner=$owner; $settings.repositoryName=$RepositoryName; $settings.repositoryUrl=$repository.html_url
    Save-Settings
    if (!$remoteUrl) { Run-Git -GitArgs @('remote','add','origin',$expectedUrl) }
    Write-Host '[4/6] 提交并上传源码改动…' -ForegroundColor Cyan
    Run-Git -GitArgs (@('add','--all','--') + $sourcePaths)
    $allTracked = @(& git -c core.quotepath=false ls-files)
    Check-PublishFiles $allTracked
    foreach ($tracked in $allTracked) {
        if (!$tracked) { continue }
        $top = ($tracked -split '/')[0]
        if ($sourcePaths -notcontains $top -and $sourcePaths -notcontains $tracked) { throw ('索引含有白名单以外的文件，请先检查：' + $tracked) }
    }
    $authorArgs = @('-c',('user.name=' + $owner),'-c',('user.email=' + $account.id + '+' + $owner + '@users.noreply.github.com'))
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) { Run-Git -GitArgs ($authorArgs + @('commit','-m',('Sync Solbrand localization ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))) }
    elseif ($LASTEXITCODE -ne 0) { throw '无法检查暂存改动。' }
    $branch = (& git symbolic-ref --short HEAD).Trim()
    $commit = (& git rev-parse HEAD).Trim()
    Push-Ref -PushArgs @('push','--set-upstream','origin',('HEAD:refs/heads/' + $branch))
    $null = Invoke-GitHub ('/repos/' + $fullName) -Method Patch -Body @{default_branch=$branch}
    $sourceHash = Get-SourceHash
    $settings.lastSourceHash=$sourceHash; $settings.lastCommit=$commit
    Save-Settings
    if ($SkipRelease) { Write-Host ('源码上传成功：' + $repository.html_url) -ForegroundColor Green; exit 0 }
    Write-Host '[5/6] 创建版本与上传 Release 文件…' -ForegroundColor Cyan
    $baseTag = 'v' + $patchVersion
    $selection = Select-GitHubReleaseTarget -FullName $fullName -BaseTag $baseTag -Commit $commit -Force ([bool]$ForceRelease)
    $tag = $selection.Tag; $revision = $selection.Revision; $release = $selection.Release
    Write-Host ('目标版本：' + $tag)
    $knownTags = @(& git tag --list $tag)
    if ($knownTags -contains $tag) {
        $tagCommit = (& git rev-list -n 1 ('refs/tags/' + $tag)).Trim()
        if ($tagCommit -ne $commit) { throw ('本地标签已指向其他提交：' + $tag) }
    } elseif ($selection.RemoteCommit) { Run-Git -GitArgs ($script:gitNetworkOptions + @('fetch','origin',('refs/tags/' + $tag + ':refs/tags/' + $tag))) }
    else { Run-Git -GitArgs ($authorArgs + @('tag','-a',$tag,'-m',('Solbrand patch ' + $patchVersion + ', assistant ' + $assistantVersion))) }
    if (!$selection.RemoteCommit) { Push-Ref -PushArgs @('push','origin',('refs/tags/' + $tag)) }
    else { Write-Host 'GitHub 上的标签已确认，无需重复推送。' }
    if (!$release) {
        $notesPath = Join-Path $taskRoot ('docs\release-v' + $patchVersion + '.md')
        $notes = if (Test-Path -LiteralPath $notesPath) { [IO.File]::ReadAllText($notesPath,[Text.Encoding]::UTF8) } else { '非官方简体中文汉化补丁与 Windows 助手。非商业性，仅供学习用途。' }
        $title = '汉化 ' + $patchVersion + ' / 助手 ' + $assistantVersion
        if ($revision -gt 1) { $title += ' · 更新 ' + $revision }
        for ($createAttempt = 1; $createAttempt -le 3; $createAttempt++) {
            Write-Host ('正在创建 Release 草稿：' + $tag + '…')
            try { $release = Invoke-GitHub ('/repos/' + $fullName + '/releases') -Method Post -Body @{tag_name=$tag;target_commitish=$commit;name=$title;body=$notes;draft=$true;prerelease=$false}; break }
            catch {
                $release = Get-GitHubRelease -FullName $fullName -Tag $tag
                if ($release) { break }
                if ($createAttempt -eq 3) { throw }
                Write-Host '草稿创建连接中断，已确认没有重复发布，准备重试…' -ForegroundColor Yellow
                Start-Sleep -Seconds 2
            }
        }
    }
    foreach ($asset in $assets) {
        $assetName = [IO.Path]::GetFileName($asset)
        $uploaded = @($release.assets | Where-Object { $_.name -eq $assetName })
        if ($uploaded.Count) {
            $digest = 'sha256:' + (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($uploaded[0].digest -ne $digest) { throw ('Release 已存在不同内容的同名文件，未覆盖：' + $assetName + '。请使用 -ForceRelease 创建新版本。') }
            continue
        }
        $uploadUrl = ($release.upload_url -split '\{')[0] + '?name=' + [Uri]::EscapeDataString($assetName)
        $digest = 'sha256:' + (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        for ($uploadAttempt = 1; $uploadAttempt -le 3; $uploadAttempt++) {
            Write-Host ('正在上传：' + $assetName + '…')
            try { $null = Invoke-GitHub $uploadUrl -Method Post -UploadFile $asset; break }
            catch {
                $current = Invoke-GitHub ('/repos/' + $fullName + '/releases/' + $release.id)
                $check = @($current.assets | Where-Object { $_.name -eq $assetName -and $_.digest -eq $digest -and $_.state -eq 'uploaded' })
                if ($check.Count) { break }
                if ($uploadAttempt -eq 3) { throw }
                Write-Host '附件上传连接中断，正在核对并重试…' -ForegroundColor Yellow
                Start-Sleep -Seconds 2
            }
        }
        Write-Host ('已上传：' + $assetName)
    }
    Write-Host '[6/6] 发布并核对结果…' -ForegroundColor Cyan
    $release = Invoke-GitHub ('/repos/' + $fullName + '/releases/' + $release.id)
    foreach ($asset in $assets) {
        $name = [IO.Path]::GetFileName($asset)
        $digest = 'sha256:' + (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
        $match = @($release.assets | Where-Object { $_.name -eq $name -and $_.digest -eq $digest })
        if (!$match.Count) { throw ('已上传但校验未完成，请在 Release 页面检查：' + $name) }
    }
    $release = Invoke-GitHub ('/repos/' + $fullName + '/releases/' + $release.id) -Method Patch -Body @{draft=$false;make_latest='true'}
    if ($release.draft) { throw 'Release 仍为草稿，请检查网络后再次运行。' }
    $settings.lastReleaseSourceHash=$sourceHash; $settings.lastReleaseUrl=$release.html_url
    Save-Settings
    Write-Host '源码与 Release 均已上传成功。' -ForegroundColor Green
    Write-Host ('仓库：' + $repository.html_url)
    Write-Host ('Release：' + $release.html_url)
    Start-Process $release.html_url
}
catch { Write-Host ''; Write-Host ('未完成：' + $_.Exception.Message) -ForegroundColor Red; exit 1 }
finally { $script:githubHeaders=$null; $credential=$null }

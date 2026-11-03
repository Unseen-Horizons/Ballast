# Copyright (c) 2026 Unseen Horizons
# This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
# distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

<#
.SYNOPSIS
    Self-test for the Ballast.Deploy module.

.DESCRIPTION
    Run it with:

        pwsh -NoProfile -File deploy/Ballast.Deploy/tests/Test-BallastDeploy.ps1

    Every az, git, dotnet, HTTP, IP and prompt call goes to a fake that records it in $script:Log. No real az, git,
    dotnet or network call happens, and nothing is pushed. The test writes only into a temp folder it creates and removes.
    The module's own temp folders are removed by the module, and the test checks that they are gone.

    Prints PASS or FAIL per check and exits 1 when any check fails.
#>

$ErrorActionPreference = 'Stop'
$pkgDir = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $pkgDir 'Ballast.Deploy.psd1') -Force
$module = Get-Module -Name 'Ballast.Deploy'

$script:Passed = 0
$script:Failed = 0
$script:Log = [System.Collections.ArrayList]::new()

# A case passes when its last boolean output is $true. Output from the module (host lines) is captured, not shown.
function Check {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][scriptblock]$Body)
    $verdict = $false
    try {
        $outputs = @(& $Body *>&1)
        $bools = @($outputs | Where-Object { $_ -is [bool] })
        $verdict = (($bools.Count -gt 0) -and ($bools[-1] -eq $true))
    }
    catch {
        Write-Host "      threw: $($_.Exception.Message)" -ForegroundColor DarkGray
    }
    if ($verdict) {
        $script:Passed++
        Write-Host "PASS  $Name" -ForegroundColor Green
    }
    else {
        $script:Failed++
        Write-Host "FAIL  $Name" -ForegroundColor Red
    }
}

# True when the block throws. Its output is discarded.
function Test-Throws {
    param([Parameter(Mandatory)][scriptblock]$Body)
    try {
        $null = & $Body *>&1
        return $false
    }
    catch {
        return $true
    }
}

function Write-TestFile {
    param([string]$Path, [string]$Content)
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { $null = New-Item -ItemType Directory -Force -Path $dir }
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Find-LogIndex {
    param([Parameter(Mandatory)][string]$Pattern)
    for ($i = 0; $i -lt $script:Log.Count; $i++) {
        if ([string]$script:Log[$i] -like $Pattern) { return $i }
    }
    return -1
}

function Measure-LogCount {
    param([Parameter(Mandatory)][string]$Pattern)
    return @($script:Log | Where-Object { ([string]$_) -like $Pattern }).Count
}

# --- Fakes -------------------------------------------------------------------------------------------------------------

function New-Result {
    param([int]$Code = 0, [string[]]$Output = @(), [string]$Err = '')
    return [pscustomobject]@{ ExitCode = $Code; Output = @($Output); ErrorText = $Err }
}

# A queue answers in order; its last answer repeats.
function Get-NextAnswer {
    param([Parameter(Mandatory)][System.Collections.Queue]$Queue)
    if ($Queue.Count -eq 0) { return $null }
    if ($Queue.Count -gt 1) { return $Queue.Dequeue() }
    return $Queue.Peek()
}

function New-Health {
    param([int]$Status = 200, [string]$Commit = '')
    $body = ''
    if ($Commit) { $body = '{"status":"Healthy","commit":"' + $Commit + '"}' }
    return [pscustomobject]@{ Status = $Status; Body = $body }
}

function Set-Az {
    param([Parameter(Mandatory)][string]$Prefix, [Parameter(Mandatory)][object]$Answer)
    $script:AzAnswers[$Prefix] = $Answer
}

$script:FakeAz = {
    param([string[]]$Arguments)
    $joined = $Arguments -join ' '
    $null = $script:Log.Add('az ' + $joined)
    $best = $null
    foreach ($prefix in @($script:AzAnswers.Keys)) {
        $text = [string]$prefix
        if ($joined.StartsWith($text, [StringComparison]::Ordinal) -and ($null -eq $best -or $text.Length -gt $best.Length)) { $best = $text }
    }
    if ($null -eq $best) { return (New-Result) }
    $answer = $script:AzAnswers[$best]
    if ($answer -is [System.Collections.Queue]) { return (Get-NextAnswer -Queue $answer) }
    return $answer
}

$script:FakeGit = {
    param([string[]]$Arguments)
    $a = @($Arguments)
    if ($a.Count -ge 2 -and $a[0] -eq '-C') { $a = @($a | Select-Object -Skip 2) }
    $null = $script:Log.Add('git ' + ($a -join ' '))
    switch ($a[0]) {
        'fetch' { return (New-Result) }
        'var' { return (New-Result -Code $script:IdentityExit -Output @('selftest <selftest@example.invalid> 0 +0000')) }
        'log' { return (New-Result -Output @($script:GitSubject)) }
        'status' { return (New-Result -Output @($script:GitDirty)) }
        'merge-base' { return (New-Result -Code $script:GitAncestorExit) }
        'for-each-ref' {
            $sha = ''
            foreach ($item in @($a)) { if ([string]$item -like '--contains=*') { $sha = ([string]$item).Substring(11) } }
            if ($script:PushedShas.ContainsKey($sha)) { return (New-Result -Output @('refs/remotes/origin/main')) }
            return (New-Result)
        }
        'rev-parse' {
            $spec = [string]$a[-1]
            if ($spec.EndsWith('^{commit}')) {
                $name = $spec.Substring(0, $spec.Length - 9)
                if ($script:GitRefs.ContainsKey($name)) { return (New-Result -Output @($script:GitRefs[$name])) }
                return (New-Result -Code 128)
            }
            if ($spec.StartsWith('refs/tags/') -and $script:LocalTags.ContainsKey($spec.Substring(10))) {
                return (New-Result -Output @('tag-object'))
            }
            return (New-Result -Code 1)
        }
        'tag' {
            if ($a[1] -eq '--list') {
                $pattern = [string]$a[2]
                return (New-Result -Output @($script:LocalTags.Keys | Where-Object { $_ -like $pattern } | Sort-Object))
            }
            if ($a[1] -eq '-a') {
                $file = [string]$a[3]
                $name = [string]$a[4]
                $sha = [string]$a[5]
                $script:TagMessages[$name] = [System.IO.File]::ReadAllText($file)
                $script:LocalTags[$name] = $sha
                $script:GitRefs[$name] = $sha
                return (New-Result)
            }
            if ($a[1] -eq '-d') {
                $null = $script:LocalTags.Remove([string]$a[2])
                return (New-Result)
            }
            return (New-Result -Code 1)
        }
        'ls-remote' {
            $lines = @($script:RemoteTags.Keys | Sort-Object | ForEach-Object { "$($script:RemoteTags[$_])`trefs/tags/$_" })
            return (New-Result -Output $lines)
        }
        'push' {
            $name = ([string]$a[-1]).Substring(10)
            if ($script:PushRefusals -gt 0) {
                $script:PushRefusals--
                return (New-Result -Code 1 -Err 'rejected: already exists (selftest refusal)')
            }
            if (-not $script:LocalTags.ContainsKey($name)) { return (New-Result -Code 1 -Err 'no such tag (selftest)') }
            $script:RemoteTags[$name] = $script:LocalTags[$name]
            return (New-Result)
        }
        'worktree' {
            if ($a[1] -eq 'add') {
                $dir = [string]$a[3]
                $null = [System.IO.Directory]::CreateDirectory($dir)
                foreach ($relative in @($script:WorktreeFiles)) {
                    $full = Join-Path $dir $relative
                    $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $full))
                    [System.IO.File]::WriteAllText($full, 'selftest')
                }
                $script:CreatedDirs += $dir
            }
            return (New-Result)
        }
        default { return (New-Result) }
    }
}

$script:FakeProcess = {
    param([string]$FilePath, [string[]]$Arguments, [hashtable]$Environment, [string]$WorkingDirectory, [bool]$Stream)
    $a = @($Arguments)
    $null = $script:Log.Add("run $FilePath " + ($a -join ' '))
    $script:LastChildEnv = $Environment
    if ($FilePath -like '*efbundle*') {
        $null = $script:Log.Add('bundle-apply')
        return (New-Result -Code $script:ApplyExit -Output @('applying (selftest)'))
    }
    if ($FilePath -ne 'dotnet') { return (New-Result) }
    switch ($a[0]) {
        'publish' {
            $out = ''
            for ($i = 0; $i -lt $a.Count - 1; $i++) { if ($a[$i] -eq '-o') { $out = [string]$a[$i + 1] } }
            $null = [System.IO.Directory]::CreateDirectory($out)
            [System.IO.File]::WriteAllText((Join-Path $out 'app.dll'), 'selftest')
            return (New-Result -Code $script:PublishExit -Output @('published (selftest)'))
        }
        'test' { return (New-Result -Code $script:GuardExit -Output $script:GuardOutput) }
        'ef' {
            if ($a[2] -eq 'list') { return (New-Result -Output (Get-NextAnswer -Queue $script:ListAnswers)) }
            if ($a[2] -eq 'bundle') {
                $out = ''
                for ($i = 0; $i -lt $a.Count - 1; $i++) { if ($a[$i] -eq '--output') { $out = [string]$a[$i + 1] } }
                $null = [System.IO.Directory]::CreateDirectory((Split-Path -Parent $out))
                [System.IO.File]::WriteAllText($out, 'bundle (selftest)')
                return (New-Result)
            }
        }
    }
    return (New-Result)
}

$script:FakeHealth = {
    param([string]$Url)
    $null = $script:Log.Add("GET $Url")
    if ($Url -like '*/health/ready*') { return [pscustomobject]@{ Status = [int](Get-NextAnswer -Queue $script:ReadyAnswers); Body = '' } }
    return (Get-NextAnswer -Queue $script:HealthAnswers)
}

$script:FakeIp = {
    param([string]$Url)
    $null = $script:Log.Add("ip $Url")
    if ($Url -like '*ipify*') { return [pscustomobject]@{ ip = '203.0.113.7' } }
    return '203.0.113.7'
}

$script:FakePrompt = {
    param([string]$Question)
    $script:PromptCount++
    $null = $script:Log.Add("prompt $Question")
    if ($Question -like 'Apply*') { return [string]$script:MigrationAnswer }
    if ($Question -like 'Deploy*') { return [string]$script:DeployAnswer }
    return [string]$script:PromptAnswer
}

function Set-ModuleRunners {
    & $module {
        param($az, $git, $process, $health, $ip, $prompt)
        $script:AzRunner = $az
        $script:GitRunner = $git
        $script:ProcessRunner = $process
        $script:HealthRunner = $health
        $script:IpRunner = $ip
        $script:PromptRunner = $prompt
    } $script:FakeAz $script:FakeGit $script:FakeProcess $script:FakeHealth $script:FakeIp $script:FakePrompt
}

function Reset-Fakes {
    $script:Log.Clear()
    $script:AzAnswers = @{}
    $script:GitRefs = @{}
    $script:LocalTags = @{}
    $script:RemoteTags = @{}
    $script:PushedShas = @{}
    $script:GitDirty = @()
    $script:GitSubject = 'Add the thing'
    $script:GitAncestorExit = 1
    $script:IdentityExit = 0
    $script:PushRefusals = 0
    $script:TagMessages = @{}
    $script:WorktreeFiles = @()
    $script:CreatedDirs = @()
    $script:PublishExit = 0
    $script:GuardExit = 0
    $script:GuardOutput = @('Passed!')
    $script:ApplyExit = 0
    $script:ListAnswers = [System.Collections.Queue]::new()
    $script:HealthAnswers = [System.Collections.Queue]::new()
    $script:ReadyAnswers = [System.Collections.Queue]::new()
    $script:PromptAnswer = ''
    $script:DeployAnswer = 'y'
    $script:MigrationAnswer = 'y'
    $script:PromptCount = 0
    $script:LastChildEnv = @{}
}

# --- Fixtures ----------------------------------------------------------------------------------------------------------

$script:Sha = 'a1b2c3d4e5f60718293a4b5c6d7e8f9012345678'
$script:OldSha = 'ffffffffffffffffffffffffffffffffffffffff'
$script:Previous = '{"id":"44444444-4444-4444-4444-444444444444","name":"Previous"}'
$script:Accounts = '[{"name":"Example Subscription","id":"11111111-1111-1111-1111-111111111111","state":"Enabled"},' +
    '{"name":"Example Subscription","id":"22222222-2222-2222-2222-222222222222","state":"Disabled"},' +
    '{"name":"Second Subscription","id":"33333333-3333-3333-3333-333333333333","state":"Enabled"}]'
$script:GoodSettingsJson = '[{"name":"WEBSITE_RUN_FROM_PACKAGE","value":"1"},{"name":"SCM_DO_BUILD_DURING_DEPLOYMENT","value":"false"},' +
    '{"name":"ENABLE_ORYX_BUILD","value":"false"},{"name":"ApiSecret","value":"sentinel-setting-value-77"}]'
$script:MigrationSettingsJson = '[{"name":"WEBSITE_RUN_FROM_PACKAGE","value":"1"},{"name":"ConnectionStrings__DefaultConnection",' +
    '"value":"Server=tcp:example-sql.database.windows.net,1433;Initial Catalog=ExampleDb;User ID=example;Password=sentinel-pw-42;"}]'
$script:PendingListing = '[{"id":"20260101000000_Initial","applied":true},{"id":"20261020000000_AddColumn","applied":false}]'
$script:AppliedListing = '[{"id":"20260101000000_Initial","applied":true},{"id":"20261020000000_AddColumn","applied":true}]'
$script:AppLines = @(
    "Subscription  = 'Example Subscription'",
    "ResourceGroup = 'example-rg'",
    "WebApp        = 'example-api'",
    "HealthUrl     = 'https://example-api.example.net'",
    "Project       = 'src/Api/Api.csproj'"
)

function New-AppsPsd1Text {
    param([string[]]$AppLines = $script:AppLines, [string[]]$OptionLines = @())
    $lines = @('@{')
    if ($OptionLines.Count -gt 0) {
        $lines += '    Options = @{'
        $lines += @($OptionLines | ForEach-Object { '        ' + $_ })
        $lines += '    }'
    }
    $lines += '    Apps = @{'
    $lines += '        api = @{'
    $lines += @($AppLines | ForEach-Object { '            ' + $_ })
    $lines += '        }'
    $lines += '    }'
    $lines += '}'
    return ($lines -join "`n")
}

# The config as an in-memory hashtable, the same shape Read-BallastConfig returns. Tags and retries pass with no delay.
function New-TestConfig {
    param([switch]$WithMigrations, [switch]$WithGuard, [switch]$TwoApps, [hashtable]$Options = @{})
    $api = @{
        Subscription  = 'Example Subscription'
        ResourceGroup = 'example-rg'
        WebApp        = 'example-api'
        HealthUrl     = 'https://example-api.example.net'
        Project       = 'src/Api/Api.csproj'
    }
    if ($WithMigrations) {
        $migrations = @{ DbContext = 'ExampleDbContext'; SqlServer = 'example-sql' }
        if ($WithGuard) { $migrations['Guard'] = @{ Project = 'tests/Guard/Guard.csproj'; Filter = '*MigrationSafetyGuardTests' } }
        $api['Migrations'] = $migrations
    }
    $apps = @{ api = $api }
    if ($TwoApps) {
        $apps['map'] = @{
            Subscription  = 'Second Subscription'
            ResourceGroup = 'example-rg'
            WebApp        = 'example-map'
            HealthUrl     = 'https://example-map.example.net'
            Project       = 'src/Map/Map.csproj'
        }
    }
    $allOptions = @{ TagPushDelaySeconds = 0; ListRetryDelaySeconds = 0 }
    foreach ($key in $Options.Keys) { $allOptions[$key] = $Options[$key] }
    return @{ Options = $allOptions; Apps = $apps }
}

function Set-DeployFixtures {
    param([switch]$WithMigrations, [switch]$Unpushed, [switch]$WithGuard)
    Reset-Fakes
    $script:GitRefs['HEAD'] = $script:Sha
    if (-not $Unpushed) { $script:PushedShas[$script:Sha] = $true }
    $script:WorktreeFiles = @('src/Api/Api.csproj', 'src/Map/Map.csproj', 'tests/Guard/Guard.csproj')
    Set-Az 'account show' (New-Result -Output @($script:Previous))
    Set-Az 'account list' (New-Result -Output @($script:Accounts))
    Set-Az 'account set' (New-Result)
    $settings = $script:GoodSettingsJson
    if ($WithMigrations) { $settings = $script:MigrationSettingsJson }
    Set-Az 'webapp config appsettings list' (New-Result -Output @($settings))
    Set-Az 'webapp deployment source config-zip' (New-Result)
    Set-Az 'sql db show' (New-Result)
    Set-Az 'sql server firewall-rule create' (New-Result)
    Set-Az 'sql server firewall-rule list' (New-Result)
    Set-Az 'sql server firewall-rule delete' (New-Result)
    # The live read, then two matching polls.
    $script:HealthAnswers.Enqueue((New-Health -Commit $script:OldSha))
    $script:HealthAnswers.Enqueue((New-Health -Commit $script:Sha))
    $script:HealthAnswers.Enqueue((New-Health -Commit $script:Sha))
    $script:ReadyAnswers.Enqueue(200)
    $script:ReadyAnswers.Enqueue(200)
    if ($WithMigrations) {
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $script:ListAnswers.Enqueue(@($script:AppliedListing))
    }
}

function Set-MigrationFixtures {
    param([string]$Settings = $script:MigrationSettingsJson)
    Reset-Fakes
    Set-Az 'account show' (New-Result -Output @($script:Previous))
    Set-Az 'account list' (New-Result -Output @($script:Accounts))
    Set-Az 'account set' (New-Result)
    Set-Az 'webapp config appsettings list' (New-Result -Output @($Settings))
    Set-Az 'sql db show' (New-Result)
    Set-Az 'sql server firewall-rule create' (New-Result)
    Set-Az 'sql server firewall-rule list' (New-Result)
    Set-Az 'sql server firewall-rule delete' (New-Result)
}

# Runs a deploy and returns its result object and the text of everything it printed.
function Invoke-DeployCaptured {
    param(
        [Parameter(Mandatory)][hashtable]$Config,
        [string[]]$App = @('api'),
        [string]$Ref = 'HEAD',
        [switch]$Yes,
        [switch]$WhatIf,
        [switch]$RefusePendingMigrations,
        [int]$TimeoutSeconds = 1
    )
    $all = @(& {
            Invoke-BallastDeploy -Config $Config -App $App -Ref $Ref -RepoPath $script:RepoDir -Yes:$Yes -WhatIf:$WhatIf `
                -RefusePendingMigrations:$RefusePendingMigrations -TimeoutSeconds $TimeoutSeconds -PollSeconds 0
        } *>&1)
    $result = $all | Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['ExitCode'] } | Select-Object -Last 1
    return [pscustomobject]@{ Result = $result; Text = ($all | Out-String) }
}

function Invoke-MigrationsCaptured {
    param([Parameter(Mandatory)][hashtable]$Config, [string]$Bundle = '', [switch]$WhatIf, [switch]$Yes)
    $all = @(& {
            Invoke-BallastMigrations -Config $Config -App 'api' -RepoPath $script:RepoDir -BundlePath $Bundle -WhatIf:$WhatIf -Yes:$Yes
        } *>&1)
    $result = $all | Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['ExitCode'] } | Select-Object -Last 1
    return [pscustomobject]@{ Result = $result; Text = ($all | Out-String) }
}

# --- Temp folders ------------------------------------------------------------------------------------------------------

$script:Root = Join-Path ([System.IO.Path]::GetTempPath()) ('ballast-deploy-selftest-' + [guid]::NewGuid().ToString('N'))
$script:RepoDir = Join-Path $script:Root 'repo'
$script:ConfigDir = Join-Path $script:Root 'config'
$script:BundleDir = Join-Path $script:Root 'bundle'
$bundlePath = Join-Path $script:BundleDir 'efbundle'

try {
    Write-TestFile -Path (Join-Path $script:RepoDir 'src/Api/Api.csproj') -Content '<Project />'
    Write-TestFile -Path (Join-Path $script:RepoDir 'src/Map/Map.csproj') -Content '<Project />'
    Write-TestFile -Path (Join-Path $script:RepoDir 'tests/Guard/Guard.csproj') -Content '<Project />'
    Write-TestFile -Path $bundlePath -Content 'bundle (selftest)'
    Set-ModuleRunners
    Reset-Fakes

    # ===== A. Configuration ===================================================================================

    Check 'Resolve-BallastConfig: TagDateFormat defaults to yyyy-MM-dd' {
        $cfg = & $module { param($c) Resolve-BallastConfig -Config $c } (New-TestConfig)
        $cfg.Options.TagDateFormat -ceq 'yyyy-MM-dd'
    }

    Check 'Read-BallastConfig: TagDateFormat yyyyMMdd is still accepted as an option' {
        $path = Join-Path $script:ConfigDir 'read-tagdate.psd1'
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -OptionLines @("TagDateFormat = 'yyyyMMdd'") -AppLines $script:AppLines)
        $cfg = Read-BallastConfig -Path $path
        $cfg.Options.TagDateFormat -ceq 'yyyyMMdd'
    }

    Check 'Read-BallastConfig: RequireCaseInsensitiveCollation is read as a bool and is off by default' {
        $withFlag = Join-Path $script:ConfigDir 'read-ci-on.psd1'
        $withoutFlag = Join-Path $script:ConfigDir 'read-ci-off.psd1'
        $migrations = "Migrations = @{ DbContext = 'ExampleDbContext'; SqlServer = 'example-sql' }"
        Write-TestFile -Path $withFlag -Content (New-AppsPsd1Text -AppLines ($script:AppLines + @($migrations, 'RequireCaseInsensitiveCollation = $true')))
        Write-TestFile -Path $withoutFlag -Content (New-AppsPsd1Text -AppLines ($script:AppLines + @($migrations)))
        $on = (Read-BallastConfig -Path $withFlag).Apps['api'].RequireCaseInsensitiveCollation
        $off = (Read-BallastConfig -Path $withoutFlag).Apps['api'].RequireCaseInsensitiveCollation
        ($on -eq $true) -and ($off -eq $false)
    }

    Check 'Read-BallastConfig: RequireCaseInsensitiveCollation must be a bool, and needs Migrations with a SqlServer' {
        $path = Join-Path $script:ConfigDir 'read-ci-bad.psd1'
        $migrations = "Migrations = @{ DbContext = 'ExampleDbContext'; SqlServer = 'example-sql' }"
        $noServer = "Migrations = @{ DbContext = 'ExampleDbContext' }"
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -AppLines ($script:AppLines + @($migrations, "RequireCaseInsensitiveCollation = 'yes'")))
        $notBool = Test-Throws { Read-BallastConfig -Path $path }
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -AppLines ($script:AppLines + @('RequireCaseInsensitiveCollation = $true')))
        $noMigrations = Test-Throws { Read-BallastConfig -Path $path }
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -AppLines ($script:AppLines + @($noServer, 'RequireCaseInsensitiveCollation = $true')))
        $noSqlServer = Test-Throws { Read-BallastConfig -Path $path }
        $notBool -and $noMigrations -and $noSqlServer
    }

    Check 'Ballast.Deploy.psd1: ModuleVersion 0.1.0 with the preview2 prerelease label' {
        $manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $pkgDir 'Ballast.Deploy.psd1')
        ($manifest.ModuleVersion -eq '0.1.0') -and ($manifest.PrivateData.PSData.Prerelease -eq 'preview2')
    }

    Check 'Read-BallastConfig: an app is read from a psd1 with its placement as written' {
        $path = Join-Path $script:ConfigDir 'read-a.psd1'
        $text = New-AppsPsd1Text -AppLines ($script:AppLines + @("LegacyBuildTimeProperty = 'ExampleBuildTimeUtc'"))
        Write-TestFile -Path $path -Content $text
        $cfg = Read-BallastConfig -Path $path
        $app = $cfg.Apps['api']
        ($app.Subscription -eq 'Example Subscription') -and ($app.ResourceGroup -eq 'example-rg') -and ($app.WebApp -eq 'example-api') -and
            ($app.HealthUrl -eq 'https://example-api.example.net') -and ($app.LegacyBuildTimeProperty -eq 'ExampleBuildTimeUtc')
    }

    Check 'Read-BallastConfig: the default ForbiddenSettings are exactly the two warm-up settings' {
        $path = Join-Path $script:ConfigDir 'read-b.psd1'
        Write-TestFile -Path $path -Content (New-AppsPsd1Text)
        $cfg = Read-BallastConfig -Path $path
        ((@($cfg.Options.ForbiddenSettings) -join ',') -eq 'WEBSITE_WARMUP_PATH,WEBSITE_WARMUP_STATUSES')
    }

    Check 'Read-BallastConfig: a ForbiddenSettings option replaces the default list' {
        $path = Join-Path $script:ConfigDir 'read-c.psd1'
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -OptionLines @("ForbiddenSettings = @('EXAMPLE_ONE')"))
        $cfg = Read-BallastConfig -Path $path
        ((@($cfg.Options.ForbiddenSettings) -join ',') -eq 'EXAMPLE_ONE')
    }

    Check 'Read-BallastConfig: an app with no WebApp is refused' {
        $path = Join-Path $script:ConfigDir 'read-d.psd1'
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -AppLines @($script:AppLines | Where-Object { $_ -notmatch 'WebApp' }))
        Test-Throws { Read-BallastConfig -Path $path }
    }

    Check 'Read-BallastConfig: an unknown option is refused, so a typo does not keep a default' {
        $path = Join-Path $script:ConfigDir 'read-e.psd1'
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -OptionLines @('TagPushAttempt = 9'))
        Test-Throws { Read-BallastConfig -Path $path }
    }

    Check 'Read-BallastConfig: an app name that cannot name a tag is refused' {
        $path = Join-Path $script:ConfigDir 'read-f.psd1'
        $text = (New-AppsPsd1Text).Replace('        api = @{', '        Bad_Name = @{')
        Write-TestFile -Path $path -Content $text
        Test-Throws { Read-BallastConfig -Path $path }
    }

    Check 'Read-BallastConfig: a LegacyBuildTimeProperty that is not a property name is refused' {
        $path = Join-Path $script:ConfigDir 'read-g.psd1'
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -AppLines ($script:AppLines + @("LegacyBuildTimeProperty = 'Bad Name'")))
        Test-Throws { Read-BallastConfig -Path $path }
    }

    Check 'Read-BallastConfig: a Project outside the repository is refused' {
        $path = Join-Path $script:ConfigDir 'read-h.psd1'
        $lines = @(
            "Subscription  = 'Example Subscription'",
            "ResourceGroup = 'example-rg'",
            "WebApp        = 'example-api'",
            "HealthUrl     = 'https://example-api.example.net'",
            "Project       = '../outside/Api.csproj'"
        )
        Write-TestFile -Path $path -Content (New-AppsPsd1Text -AppLines $lines)
        Test-Throws { Read-BallastConfig -Path $path }
    }

    Check 'Read-BallastConfig: a HealthUrl with a path or without https is refused' {
        $path = Join-Path $script:ConfigDir 'read-i.psd1'
        $withPath = New-AppsPsd1Text -AppLines @($script:AppLines | ForEach-Object { $_ -replace "https://example-api.example.net'", "https://example-api.example.net/health'" })
        Write-TestFile -Path $path -Content $withPath
        $threw = Test-Throws { Read-BallastConfig -Path $path }
        $plain = New-AppsPsd1Text -AppLines @($script:AppLines | ForEach-Object { $_ -replace 'https://', 'http://' })
        Write-TestFile -Path $path -Content $plain
        $threw -and (Test-Throws { Read-BallastConfig -Path $path })
    }

    Check 'The module names no app, no subscription id and no placement' {
        $text = Get-Content -Raw -LiteralPath (Join-Path $pkgDir 'Ballast.Deploy.psm1')
        ($text -notmatch '(?i)getthere|transitgraph|transitinfo|chistify|azurewebsites|zagreb') -and
            ($text -notmatch '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}') -and
            ($text -notmatch 'example-')
    }

    Check 'The warm-up settings are named only in the forbidden default, never in advice' {
        $lines = @(Get-Content -LiteralPath (Join-Path $pkgDir 'Ballast.Deploy.psm1') | Where-Object { $_ -match 'WEBSITE_WARMUP' })
        ($lines.Count -eq 1) -and ($lines[0] -match "ForbiddenSettings\s*=\s*@\('WEBSITE_WARMUP_PATH', 'WEBSITE_WARMUP_STATUSES'\)")
    }

    # ===== B. Secrets ==========================================================================================

    Check 'Hide-BallastSecret: a bearer token is redacted' {
        $safe = Hide-BallastSecret -Text 'Authorization: Bearer eyJhbGci.sentinel-token-1=='
        ($safe -notmatch 'sentinel-token-1') -and ($safe -match '<redacted>')
    }

    Check 'Hide-BallastSecret: AccountKey= and SharedAccessSignature= values are redacted, the key names stay' {
        $safe = Hide-BallastSecret -Text 'DefaultEndpoints;AccountKey=sentinel-key-2==;SharedAccessSignature=sentinel-sas-3'
        ($safe -notmatch 'sentinel-key-2') -and ($safe -notmatch 'sentinel-sas-3') -and ($safe -match 'AccountKey=<redacted>') -and ($safe -match 'SharedAccessSignature=<redacted>')
    }

    Check 'Hide-BallastSecret: password= and pwd= values are redacted' {
        $safe = Hide-BallastSecret -Text 'Server=x;Password=sentinel-pw-4;pwd=sentinel-pw-5;'
        ($safe -notmatch 'sentinel-pw-4') -and ($safe -notmatch 'sentinel-pw-5') -and ($safe -match 'Password=<redacted>')
    }

    Check 'Hide-BallastSecret: a pattern list given for one call replaces the defaults for that call' {
        $safe = Hide-BallastSecret -Text 'password=kept token=sentinel-tok-6' -Patterns @('(?<=token=)\S+')
        ($safe -match 'password=kept') -and ($safe -notmatch 'sentinel-tok-6')
    }

    Check 'Invoke-Az: a failing call throws with its stderr, and the secret in it is hidden' {
        Reset-Fakes
        Set-Az 'account show' (New-Result -Code 1 -Err 'failed: Password=sentinel-err-7')
        $threw = $false
        $text = ''
        try { $null = & $module { Invoke-Az -Arguments @('account', 'show') } }
        catch { $threw = $true; $text = $_.Exception.Message }
        $threw -and ($text -notmatch 'sentinel-err-7') -and ($text -match '<redacted>')
    }

    # ===== C. Runner wrappers and subscriptions ================================================================

    Check 'Invoke-Git: -AllowFailure returns the exit code instead of throwing' {
        Reset-Fakes
        $result = & $module { Invoke-Git -RepoPath '.' -Arguments @('merge-base', '--is-ancestor', 'x', 'y') -AllowFailure }
        ($result.ExitCode -eq 1) -and ((Measure-LogCount 'git *merge-base*') -eq 1)
    }

    Check 'Use-BallastSubscription: selects the enabled match before other calls and returns the previous default' {
        Reset-Fakes
        Set-Az 'account show' (New-Result -Output @($script:Previous))
        Set-Az 'account list' (New-Result -Output @($script:Accounts))
        Set-Az 'account set' (New-Result)
        $previous = & $module { Use-BallastSubscription -Subscription 'Example Subscription' }
        ($previous -eq '44444444-4444-4444-4444-444444444444') -and
            ((Measure-LogCount 'az account set --subscription 11111111-1111-1111-1111-111111111111 *') -eq 1) -and
            ((Measure-LogCount '*22222222-2222-2222-2222-222222222222*') -eq 0)
    }

    Check 'Select-BallastSubscriptionId: a name that matches only a disabled subscription is refused' {
        $only = @([pscustomobject]@{ name = 'Retired'; id = '55555555-5555-5555-5555-555555555555'; state = 'Disabled' })
        Test-Throws { & $module { param($a) Select-BallastSubscriptionId -Accounts $a -Name 'Retired' } $only }
    }

    # ===== D. Deploy guards ====================================================================================

    Check 'Test-BallastForbiddenSettings: WEBSITE_WARMUP_PATH refuses the app' {
        $settings = @{ WEBSITE_RUN_FROM_PACKAGE = '1'; WEBSITE_WARMUP_PATH = '/health/ready' }
        $result = & $module { param($s) Test-BallastForbiddenSettings -Settings $s -Forbidden @('WEBSITE_WARMUP_PATH', 'WEBSITE_WARMUP_STATUSES') -ResourceGroup 'rg' -WebApp 'app' } $settings
        -not $result.Ok
    }

    Check 'Test-BallastForbiddenSettings: WEBSITE_WARMUP_STATUSES refuses the app, even when empty' {
        $settings = @{ WEBSITE_WARMUP_STATUSES = '' }
        $result = & $module { param($s) Test-BallastForbiddenSettings -Settings $s -Forbidden @('WEBSITE_WARMUP_PATH', 'WEBSITE_WARMUP_STATUSES') -ResourceGroup 'rg' -WebApp 'app' } $settings
        -not $result.Ok
    }

    Check 'Test-BallastForbiddenSettings: a clean app passes' {
        $settings = @{ WEBSITE_RUN_FROM_PACKAGE = '1' }
        $result = & $module { param($s) Test-BallastForbiddenSettings -Settings $s -Forbidden @('WEBSITE_WARMUP_PATH', 'WEBSITE_WARMUP_STATUSES') -ResourceGroup 'rg' -WebApp 'app' } $settings
        $result.Ok -eq $true
    }

    Check 'Test-BallastForbiddenSettings: the refusal says to remove the setting and never to set it' {
        $settings = @{ WEBSITE_WARMUP_PATH = '/x' }
        $result = & $module { param($s) Test-BallastForbiddenSettings -Settings $s -Forbidden @('WEBSITE_WARMUP_PATH') -ResourceGroup 'rg' -WebApp 'app' } $settings
        $text = [string]$result.Lines[0].Text
        ($text -match 'Remove it') -and ($text -match 'appsettings delete') -and ($text -notmatch 'appsettings set') -and ($text -notmatch 'WEBSITE_WARMUP_PATH=')
    }

    Check 'Test-BallastRunFromPackage: a URL in WEBSITE_RUN_FROM_PACKAGE is refused' {
        $settings = @{ WEBSITE_RUN_FROM_PACKAGE = 'https://sentinel-zip-8.example.invalid/pkg.zip' }
        $result = & $module { param($s) Test-BallastRunFromPackage -Settings $s -WebApp 'app' } $settings
        -not $result.Ok
    }

    Check 'Test-BallastRunFromPackage: a missing WEBSITE_RUN_FROM_PACKAGE is refused' {
        $result = & $module { param($s) Test-BallastRunFromPackage -Settings $s -WebApp 'app' } @{}
        -not $result.Ok
    }

    Check 'Test-BallastRunFromPackage: a server-side build (SCM_DO_BUILD_DURING_DEPLOYMENT=true) is refused, and 1 passes' {
        $bad = & $module { param($s) Test-BallastRunFromPackage -Settings $s -WebApp 'app' } @{ WEBSITE_RUN_FROM_PACKAGE = '1'; SCM_DO_BUILD_DURING_DEPLOYMENT = 'true' }
        $good = & $module { param($s) Test-BallastRunFromPackage -Settings $s -WebApp 'app' } @{ WEBSITE_RUN_FROM_PACKAGE = '1' }
        (-not $bad.Ok) -and $good.Ok
    }

    Check 'The run-from-package lines carry no setting value' {
        $settings = @{ WEBSITE_RUN_FROM_PACKAGE = 'https://sentinel-zip-9.example.invalid/x'; ApiSecret = 'sentinel-secret-10' }
        $result = & $module { param($s) Test-BallastRunFromPackage -Settings $s -WebApp 'app' } $settings
        $text = (@($result.Lines) | ForEach-Object { $_.Text }) -join "`n"
        ($text -notmatch 'sentinel-zip-9') -and ($text -notmatch 'sentinel-secret-10')
    }

    # ===== E. Build and zip ====================================================================================

    Check 'New-BallastDeploymentZip: entries use forward slashes and keep nested files' {
        $src = Join-Path $script:Root 'zipsrc'
        Write-TestFile -Path (Join-Path $src 'a/b/c.txt') -Content 'nested'
        Write-TestFile -Path (Join-Path $src 'top.txt') -Content 'top'
        $zipPath = Join-Path $script:Root 'out.zip'
        $count = & $module { param($s, $z) New-BallastDeploymentZip -SourceDir $s -ZipPath $z } $src $zipPath
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $names = @($zip.Entries | ForEach-Object { $_.FullName })
        }
        finally { $zip.Dispose() }
        ($count -eq 2) -and ($names -contains 'a/b/c.txt') -and ($names -contains 'top.txt') -and (@($names | Where-Object { $_ -match '\\' }).Count -eq 0)
    }

    Check 'Test-BallastBuildTime: the exact yyyy-MM-ddTHH:mm:ssZ form passes' {
        & $module { Test-BallastBuildTime -Value '2026-10-21T10:00:00Z' }
    }

    Check 'Test-BallastBuildTime: a stamp that is not exact is refused (space, fraction, offset, impossible date)' {
        $bad = @('2026-10-21 10:00:00', '2026-10-21T10:00:00.000Z', '2026-10-21T10:00:00+00:00', '2026-02-30T10:00:00Z')
        $refused = @($bad | Where-Object { -not (& $module { param($v) Test-BallastBuildTime -Value $v } $_) })
        $refused.Count -eq $bad.Count
    }

    Check 'Invoke-BallastPublish: a stamp that is not exact is refused and dotnet is not run' {
        Reset-Fakes
        $threw = Test-Throws { & $module { Invoke-BallastPublish -WorktreeDir 'w' -Project 'p.csproj' -OutDir 'o' -Sha ('a' * 40) -BuildTimeUtc '2026-10-21 10:00:00' } }
        $threw -and ((Measure-LogCount 'run dotnet publish*') -eq 0)
    }

    Check 'Invoke-BallastPublish: the stamp is BallastBuildTimeUtc and the commit is SourceRevisionId' {
        Reset-Fakes
        $out = Join-Path $script:Root 'pub-a'
        & $module { param($r, $o, $s) Invoke-BallastPublish -WorktreeDir $r -Project 'src/Api/Api.csproj' -OutDir $o -Sha $s -BuildTimeUtc '2026-10-21T10:00:00Z' } $script:RepoDir $out $script:Sha
        ((Measure-LogCount '*-p:BallastBuildTimeUtc=2026-10-21T10:00:00Z*') -eq 1) -and ((Measure-LogCount "*-p:SourceRevisionId=$($script:Sha)*") -eq 1)
    }

    Check 'Invoke-BallastPublish: the legacy build property gets the same value only when configured' {
        Reset-Fakes
        $out = Join-Path $script:Root 'pub-b'
        & $module { param($r, $o, $s) Invoke-BallastPublish -WorktreeDir $r -Project 'src/Api/Api.csproj' -OutDir $o -Sha $s -BuildTimeUtc '2026-10-21T10:00:00Z' -LegacyBuildTimeProperty 'ExampleBuildTimeUtc' } $script:RepoDir $out $script:Sha
        $withLegacy = Measure-LogCount '*-p:ExampleBuildTimeUtc=2026-10-21T10:00:00Z*'
        Reset-Fakes
        & $module { param($r, $o, $s) Invoke-BallastPublish -WorktreeDir $r -Project 'src/Api/Api.csproj' -OutDir $o -Sha $s -BuildTimeUtc '2026-10-21T10:00:00Z' } $script:RepoDir $out $script:Sha
        $withoutLegacy = Measure-LogCount '*ExampleBuildTimeUtc*'
        ($withLegacy -eq 1) -and ($withoutLegacy -eq 0)
    }

    Check 'Invoke-BallastPublish: a sha that is not a full lowercase sha is refused' {
        Reset-Fakes
        Test-Throws { & $module { Invoke-BallastPublish -WorktreeDir 'w' -Project 'p.csproj' -OutDir 'o' -Sha 'ABC' -BuildTimeUtc '2026-10-21T10:00:00Z' } }
    }

    Check 'The build worktree is added detached and removed with --force, and its root is gone afterwards' {
        Reset-Fakes
        $script:WorktreeFiles = @('src/Api/Api.csproj')
        $root = Join-Path $script:Root 'wt-root'
        $dir = & $module { param($r, $s) New-BallastBuildWorktree -Sha $s -Root $r -RepoPath '.' } $root $script:Sha
        $existed = Test-Path -LiteralPath $dir
        & $module { param($r) Remove-BallastBuildWorktree -Root $r -RepoPath '.' } $root
        ($existed) -and ((Measure-LogCount 'git worktree add --detach *') -eq 1) -and ((Measure-LogCount 'git worktree remove --force *') -eq 1) -and (-not (Test-Path -LiteralPath $root))
    }

    # ===== F. Commit checks and refs ============================================================================

    Check 'Assert-BallastDeployableCommit: a commit that is not on origin is refused' {
        Reset-Fakes
        Test-Throws { & $module { param($s) Assert-BallastDeployableCommit -Sha $s -RepoPath '.' } $script:Sha }
    }

    Check 'Assert-BallastDeployableCommit: a pushed commit passes, and uncommitted changes only warn' {
        Reset-Fakes
        $script:PushedShas[$script:Sha] = $true
        $script:GitDirty = @(' M some-file.txt')
        $out = @(& $module { param($s) Assert-BallastDeployableCommit -Sha $s -RepoPath '.' } $script:Sha *>&1)
        $text = $out | Out-String
        ($text -match 'not part of this deploy')
    }

    Check 'Resolve-BallastRef: a deploy tag for another app is refused, and the tag for its own app is accepted' {
        Reset-Fakes
        $script:GitRefs['deploy/api/20261021-1'] = $script:Sha
        $script:LocalTags['deploy/api/20261021-1'] = $script:Sha
        $refused = Test-Throws { & $module { Resolve-BallastRef -Ref 'deploy/api/20261021-1' -App @('map') -RepoPath '.' } }
        $ok = & $module { Resolve-BallastRef -Ref 'deploy/api/20261021-1' -App @('api') -RepoPath '.' }
        $refused -and ($ok.TagApp -eq 'api') -and ($ok.Sha -eq $script:Sha)
    }

    Check 'Resolve-BallastRef: a deploy tag is refused when several apps are named' {
        Reset-Fakes
        $script:GitRefs['deploy/api/20261021-1'] = $script:Sha
        $script:LocalTags['deploy/api/20261021-1'] = $script:Sha
        Test-Throws { & $module { Resolve-BallastRef -Ref 'deploy/api/20261021-1' -App @('api', 'map') -RepoPath '.' } }
    }

    # ===== G. Deploy tags ======================================================================================

    Check 'Get-BallastTagDateText: yyyyMMdd and yyyy-MM-dd give the two accepted forms' {
        $when = [DateTime]::new(2026, 10, 22)
        (& $module { param($w) Get-BallastTagDateText -Format 'yyyyMMdd' -When $w } $when) -eq '20261022'
        (& $module { param($w) Get-BallastTagDateText -Format 'yyyy-MM-dd' -When $w } $when) -eq '2026-10-22'
    }

    Check 'Get-BallastNextTagName: the first tag of a day is -1' {
        Reset-Fakes
        (& $module { Get-BallastNextTagName -App 'api' -DateText '20261022' -RepoPath '.' }) -eq 'deploy/api/20261022-1'
    }

    Check 'Get-BallastNextTagName: a -1 that only origin has gives -2, and another app does not count' {
        Reset-Fakes
        $script:RemoteTags['deploy/api/20261022-1'] = $script:Sha
        $script:RemoteTags['deploy/map/20261022-9'] = $script:Sha
        (& $module { Get-BallastNextTagName -App 'api' -DateText '20261022' -RepoPath '.' }) -eq 'deploy/api/20261022-2'
    }

    Check 'Get-BallastNextTagName: local -3 and remote -1 give -4' {
        Reset-Fakes
        $script:RemoteTags['deploy/api/20261022-1'] = $script:Sha
        $script:LocalTags['deploy/api/20261022-3'] = $script:Sha
        (& $module { Get-BallastNextTagName -App 'api' -DateText '20261022' -RepoPath '.' }) -eq 'deploy/api/20261022-4'
    }

    Check 'New-BallastDeployTag: a refused push retries, removing the abandoned local tag, and pushes the next free number' {
        Reset-Fakes
        $script:PushRefusals = 1
        $message = @('commit: selftest', 'ref: HEAD')
        $tag = & $module { param($s, $m) New-BallastDeployTag -App 'api' -Sha $s -Message $m -DateText '20261022' -RepoPath '.' -Attempts 3 -DelaySeconds 0 } $script:Sha $message
        ($tag.Pushed -eq $true) -and ($tag.Name -eq 'deploy/api/20261022-1') -and ((Measure-LogCount 'git tag -d deploy/api/20261022-1') -eq 1) -and
            ((Measure-LogCount 'git push origin refs/tags/deploy/api/20261022-1') -eq 2)
    }

    Check 'New-BallastDeployTag: when every attempt is refused, the last tag is kept for the owner and Pushed is false' {
        Reset-Fakes
        $script:PushRefusals = 5
        $tag = & $module { param($s) New-BallastDeployTag -App 'api' -Sha $s -Message @('commit: x') -DateText '20261022' -RepoPath '.' -Attempts 2 -DelaySeconds 0 } $script:Sha
        ($tag.Pushed -eq $false) -and ($tag.Name -eq 'deploy/api/20261022-1') -and $script:LocalTags.ContainsKey('deploy/api/20261022-1')
    }

    Check 'New-BallastDeployTag: the tag message holds the lines given' {
        Reset-Fakes
        $null = & $module { param($s) New-BallastDeployTag -App 'api' -Sha $s -Message @('commit: selftest', 'ref: HEAD') -DateText '20261022' -RepoPath '.' -Attempts 1 -DelaySeconds 0 } $script:Sha
        $text = [string]$script:TagMessages['deploy/api/20261022-1']
        ($text -match 'commit: selftest') -and ($text -match 'ref: HEAD')
    }

    Check 'New-BallastDeployTag: no committer identity is refused before any tag is made' {
        Reset-Fakes
        $script:IdentityExit = 1
        $threw = Test-Throws { & $module { param($s) New-BallastDeployTag -App 'api' -Sha $s -Message @('x') -DateText '20261022' -RepoPath '.' } $script:Sha }
        $threw -and ((Measure-LogCount 'git tag -a*') -eq 0)
    }

    # ===== H. Health and the switch ============================================================================

    Check 'Get-BallastHealthCommit: a body with no commit, a blank commit, or no JSON gives null' {
        $a = & $module { Get-BallastHealthCommit -Json '{"status":"Healthy"}' }
        $b = & $module { Get-BallastHealthCommit -Json '{"commit":"   "}' }
        $c = & $module { Get-BallastHealthCommit -Json '<html>503</html>' }
        ($null -eq $a) -and ($null -eq $b) -and ($null -eq $c)
    }

    Check 'Get-BallastHealthCommit: a commit is read, lower-cased' {
        (& $module { param($j) Get-BallastHealthCommit -Json $j } ('{"commit":"' + $script:Sha.ToUpperInvariant() + '"}')) -eq $script:Sha
    }

    Check 'Test-BallastCommitMatches: seven characters match, six do not, a different commit does not' {
        $seven = & $module { param($r, $e) Test-BallastCommitMatches -Reported $r -Expected $e } $script:Sha.Substring(0, 7) $script:Sha
        $six = & $module { param($r, $e) Test-BallastCommitMatches -Reported $r -Expected $e } $script:Sha.Substring(0, 6) $script:Sha
        $other = & $module { param($r, $e) Test-BallastCommitMatches -Reported $r -Expected $e } $script:OldSha $script:Sha
        $seven -and (-not $six) -and (-not $other)
    }

    Check 'Wait-BallastAppSwitched: proven after two matching polls, with the ready answer 200' {
        Reset-Fakes
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:Sha))
        $script:ReadyAnswers.Enqueue(200)
        $proof = Wait-BallastAppSwitched -HealthUrl 'https://example-api.example.net' -Commit $script:Sha -TimeoutSeconds 5 -PollSeconds 0 *>&1 |
            Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['Switched'] } | Select-Object -Last 1
        ($proof.ExitCode -eq 0) -and ($proof.Switched -eq $true) -and ((Measure-LogCount 'GET https://example-api.example.net/health/ready') -eq 2)
    }

    Check 'Wait-BallastAppSwitched: one matching poll is not enough, a poll for the old commit resets the count' {
        Reset-Fakes
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:Sha))
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:OldSha))
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:Sha))
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:Sha))
        $script:ReadyAnswers.Enqueue(200)
        $proof = Wait-BallastAppSwitched -HealthUrl 'https://example-api.example.net' -Commit $script:Sha -TimeoutSeconds 5 -PollSeconds 0 *>&1 |
            Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['Switched'] } | Select-Object -Last 1
        ($proof.Switched -eq $true) -and ((Measure-LogCount 'GET *health?gt=*') -eq 4)
    }

    Check 'Wait-BallastAppSwitched: a copy that never switches times out with exit 3' {
        Reset-Fakes
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:OldSha))
        $script:ReadyAnswers.Enqueue(200)
        $proof = Wait-BallastAppSwitched -HealthUrl 'https://example-api.example.net' -Commit $script:Sha -TimeoutSeconds 1 -PollSeconds 0 *>&1 |
            Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['Switched'] } | Select-Object -Last 1
        ($proof.ExitCode -eq 3) -and ($proof.Switched -eq $false)
    }

    Check 'Wait-BallastAppSwitched: a ready answer other than 200 is not a proof, and neither is a health answer with no commit' {
        Reset-Fakes
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:Sha))
        $script:ReadyAnswers.Enqueue(503)
        $notReady = Wait-BallastAppSwitched -HealthUrl 'https://example-api.example.net' -Commit $script:Sha -TimeoutSeconds 1 -PollSeconds 0 *>&1 |
            Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['Switched'] } | Select-Object -Last 1
        Reset-Fakes
        $script:HealthAnswers.Enqueue((New-Health -Status 200))
        $script:ReadyAnswers.Enqueue(200)
        $noCommit = Wait-BallastAppSwitched -HealthUrl 'https://example-api.example.net' -Commit $script:Sha -TimeoutSeconds 1 -PollSeconds 0 *>&1 |
            Where-Object { $_ -is [pscustomobject] -and $_.PSObject.Properties['Switched'] } | Select-Object -Last 1
        ($notReady.ExitCode -eq 3) -and ($noCommit.ExitCode -eq 3)
    }

    # ===== I. Migrations =======================================================================================

    Check 'ConvertFrom-BallastMigrationListing: reads the JSON between warnings, and a null applied is unknown' {
        $lines = @('warning: something', '[{"id":"a","applied":true},{"id":"b","applied":null}]', 'trailing text')
        $parsed = & $module { param($l) ConvertFrom-BallastMigrationListing -Lines $l } $lines
        (@($parsed.Entries).Count -eq 2) -and ($null -eq $parsed.Entries[1].applied)
    }

    Check 'ConvertFrom-BallastMigrationListing: output with no JSON array gives null' {
        $null -eq (& $module { ConvertFrom-BallastMigrationListing -Lines @('no json here') })
    }

    Check 'Invoke-BallastMigrationGuard: only exact, case-sensitive marker lines are read, and a failed run is failed' {
        Reset-Fakes
        $script:GuardExit = 1
        $script:GuardOutput = @(
            'MIGRATION-GUARD 20261020000000_AddColumn drops a column',
            'MIGRATION-GUARDED nope',
            'MIGRATION-ALLOW-LIST 20261020000000_AddColumn reviewed',
            'migration-guard lower case is not a marker'
        )
        $guard = & $module { param($r, $g) Invoke-BallastMigrationGuard -RepoPath $r -Guard $g } $script:RepoDir @{ Project = 'tests/Guard/Guard.csproj'; Filter = '*Guard*' }
        ($guard.Verdict -eq 'failed') -and (@($guard.Markers).Count -eq 2) -and ($guard.Markers[0] -like 'MIGRATION-GUARD *')
    }

    Check 'Invoke-BallastMigrationGuard: a filter that matches no tests is unavailable, not clean' {
        Reset-Fakes
        $script:GuardOutput = @('No test matches the given testcase filter')
        $guard = & $module { param($r, $g) Invoke-BallastMigrationGuard -RepoPath $r -Guard $g } $script:RepoDir @{ Project = 'tests/Guard/Guard.csproj'; Filter = '*Nothing*' }
        $guard.Verdict -eq 'unavailable'
    }

    Check 'Invoke-BallastMigrationGuard: no guard configured is unavailable' {
        $guard = & $module { param($r) Invoke-BallastMigrationGuard -RepoPath $r -Guard $null } $script:RepoDir
        $guard.Verdict -eq 'unavailable'
    }

    Check 'Invoke-BallastMigrations: the prompt defaults to no, nothing is applied, exit 2 (declined)' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $script:PromptAnswer = ''
        $script:MigrationAnswer = ''
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath
        ($out.Result.ExitCode -eq 2) -and ($out.Result.Result -eq 'declined') -and ((Measure-LogCount 'bundle-apply') -eq 0) -and ($script:PromptCount -eq 1)
    }

    Check 'Invoke-BallastMigrations: y applies the bundle, the post-check passes, exit 0' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $script:ListAnswers.Enqueue(@($script:AppliedListing))
        $script:MigrationAnswer = 'y'
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath
        ($out.Result.ExitCode -eq 0) -and ($out.Result.Result -eq 'applied') -and ((Measure-LogCount 'bundle-apply') -eq 1) -and ($out.Result.Pending -eq 1)
    }

    Check 'Invoke-BallastMigrations: -Yes applies without asking' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $script:ListAnswers.Enqueue(@($script:AppliedListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -Yes
        ($out.Result.ExitCode -eq 0) -and ($script:PromptCount -eq 0) -and ((Measure-LogCount 'bundle-apply') -eq 1)
    }

    Check 'Invoke-BallastMigrations: -WhatIf stops after the summary with exit 0, applies nothing and does not ask' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -WhatIf
        ($out.Result.ExitCode -eq 0) -and ($out.Result.Result -eq 'whatif') -and ($out.Result.Pending -eq 1) -and ((Measure-LogCount 'bundle-apply') -eq 0) -and ($script:PromptCount -eq 0)
    }

    Check 'Invoke-BallastMigrations: nothing pending is exit 0 with no prompt' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:AppliedListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath
        ($out.Result.ExitCode -eq 0) -and ($out.Result.Result -eq 'nothing-pending') -and ($script:PromptCount -eq 0)
    }

    Check 'Invoke-BallastMigrations: a failed guard stops with exit 1 and applies nothing' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $script:GuardExit = 1
        $script:GuardOutput = @('MIGRATION-GUARD 20261020000000_AddColumn drops a column')
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -Yes
        ($out.Result.ExitCode -eq 1) -and ($out.Result.Result -eq 'failed') -and ((Measure-LogCount 'bundle-apply') -eq 0)
    }

    Check 'Invoke-BallastMigrations: every migration pending with -Yes is refused (exit 1)' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@('[{"id":"a","applied":false},{"id":"b","applied":false}]'))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -Yes
        ($out.Result.ExitCode -eq 1) -and ((Measure-LogCount 'bundle-apply') -eq 0)
    }

    Check 'Invoke-BallastMigrations: an unavailable guard with -Yes is refused (exit 1)' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations) -Bundle $bundlePath -Yes
        ($out.Result.ExitCode -eq 1) -and ((Measure-LogCount 'bundle-apply') -eq 0)
    }

    Check 'Invoke-BallastMigrations: a refused connection is retried, then the list is read (-WhatIf, exit 0)' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@('Cannot open server example-sql requested by the login (40615)'))
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -WhatIf
        ($out.Result.ExitCode -eq 0) -and ($out.Result.Pending -eq 1) -and ((Measure-LogCount 'run dotnet ef migrations list*') -eq 2)
    }

    Check 'Invoke-BallastMigrations: a list that is not JSON and is not a refused connection is exit 1' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@('Build failed: missing package'))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -WhatIf
        $out.Result.ExitCode -eq 1
    }

    Check 'Invoke-BallastMigrations: the APPLY-MIGRATIONS line carries the result, the app and the pending count' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -WhatIf
        $out.Text -match 'APPLY-MIGRATIONS result=whatif app=api pending=1'
    }

    Check 'Invoke-BallastMigrations: a case-sensitive database is refused before any firewall rule (exit 1)' {
        Set-MigrationFixtures
        Set-Az 'sql db show -g example-rg -s example-sql -n ExampleDb --query collation' (New-Result -Output @('SQL_Latin1_General_CP1_CS_AS'))
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $config = New-TestConfig -WithMigrations -WithGuard
        $config.Apps['api']['RequireCaseInsensitiveCollation'] = $true
        $out = Invoke-MigrationsCaptured -Config $config -Bundle $bundlePath -WhatIf
        ($out.Result.ExitCode -eq 1) -and ($out.Result.Result -eq 'failed') -and ($out.Text -match "has collation 'SQL_Latin1_General_CP1_CS_AS'") -and
            ((Measure-LogCount 'az sql server firewall-rule create*') -eq 0) -and ((Measure-LogCount 'run dotnet ef migrations list*') -eq 0)
    }

    Check 'Invoke-BallastMigrations: a case-insensitive database goes on to the firewall rule (exit 0)' {
        Set-MigrationFixtures
        Set-Az 'sql db show -g example-rg -s example-sql -n ExampleDb --query collation' (New-Result -Output @('SQL_Latin1_General_CP1_CI_AS'))
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $config = New-TestConfig -WithMigrations -WithGuard
        $config.Apps['api']['RequireCaseInsensitiveCollation'] = $true
        $out = Invoke-MigrationsCaptured -Config $config -Bundle $bundlePath -WhatIf
        ($out.Result.ExitCode -eq 0) -and ((Measure-LogCount 'az sql db show -g example-rg -s example-sql -n ExampleDb --query collation*') -eq 1) -and
            ((Measure-LogCount 'az sql server firewall-rule create*') -eq 1)
    }

    Check 'Invoke-BallastMigrations: a collation that cannot be read is refused (exit 1)' {
        Set-MigrationFixtures
        Set-Az 'sql db show -g example-rg -s example-sql -n ExampleDb --query collation' (New-Result)
        $config = New-TestConfig -WithMigrations -WithGuard
        $config.Apps['api']['RequireCaseInsensitiveCollation'] = $true
        $out = Invoke-MigrationsCaptured -Config $config -Bundle $bundlePath -WhatIf
        ($out.Result.ExitCode -eq 1) -and ($out.Text -match 'Could not read the collation') -and ((Measure-LogCount 'az sql server firewall-rule create*') -eq 0)
    }

    Check 'Invoke-BallastMigrations: without the flag the collation is not read at all' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -WhatIf
        ($out.Result.ExitCode -eq 0) -and ((Measure-LogCount '*--query collation*') -eq 0)
    }

    Check 'Invoke-BallastMigrations: the connection string and its password are never printed' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -WhatIf
        ($out.Text -notmatch 'sentinel-pw-42') -and ($out.Text -notmatch 'Server=tcp')
    }

    Check 'Invoke-BallastMigrations: the database is checked, then a temporary firewall rule is created and removed' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -WhatIf
        $db = Find-LogIndex 'az sql db show*'
        $create = Find-LogIndex 'az sql server firewall-rule create*'
        $delete = Find-LogIndex 'az sql server firewall-rule delete*'
        ($db -ge 0) -and ($create -gt $db) -and ($delete -gt $create) -and ($out.Result.ExitCode -eq 0)
    }

    Check 'Invoke-BallastMigrations: a stale firewall rule from an earlier run is removed' {
        Set-MigrationFixtures
        $stale = New-Object System.Collections.Queue
        $stale.Enqueue((New-Result -Output @('ballast-deploy-20200101T000000Z-abcd')))
        $stale.Enqueue((New-Result))
        Set-Az 'sql server firewall-rule list' $stale
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath -WhatIf
        ((Measure-LogCount 'az sql server firewall-rule delete * -n ballast-deploy-20200101T000000Z-abcd *') -eq 1) -and ($out.Result.ExitCode -eq 0)
    }

    Check 'Invoke-BallastMigrations: the az default subscription is put back at the end' {
        Set-MigrationFixtures
        $script:ListAnswers.Enqueue(@($script:AppliedListing))
        $null = Invoke-MigrationsCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Bundle $bundlePath
        (Measure-LogCount 'az account set --subscription 44444444-4444-4444-4444-444444444444 *') -ge 1
    }

    # ===== J. Deploy ===========================================================================================

    Check 'Invoke-BallastDeploy: an app name that is not in the config is exit 1, and nothing runs' {
        Set-DeployFixtures
        $out = Invoke-DeployCaptured -Config (New-TestConfig) -App @('nosuch') -Yes
        ($out.Result.ExitCode -eq 1) -and ((Measure-LogCount 'az *') -eq 0)
    }

    Check 'Invoke-BallastDeploy: an app named twice is exit 1, and an empty -App is refused when bound' {
        Set-DeployFixtures
        $twice = Invoke-DeployCaptured -Config (New-TestConfig -TwoApps) -App @('api', 'api') -Yes
        $emptyRefused = Test-Throws { Invoke-BallastDeploy -Config (New-TestConfig) -App @() -RepoPath $script:RepoDir -Yes }
        ($twice.Result.ExitCode -eq 1) -and $emptyRefused -and ((Measure-LogCount 'az *') -eq 0)
    }

    Check 'Invoke-BallastDeploy: a config that is not valid is exit 1 and nothing runs' {
        Set-DeployFixtures
        $bad = New-TestConfig
        $bad.Apps['api'].Remove('WebApp')
        $out = Invoke-DeployCaptured -Config $bad -Yes
        ($out.Result.ExitCode -eq 1) -and ((Measure-LogCount 'az *') -eq 0)
    }

    Check 'Invoke-BallastDeploy: an unpushed commit is exit 1, and nothing is built or deployed' {
        Set-DeployFixtures -Unpushed
        $out = Invoke-DeployCaptured -Config (New-TestConfig) -Yes
        ($out.Result.ExitCode -eq 1) -and ((Measure-LogCount 'run dotnet publish*') -eq 0) -and ((Measure-LogCount 'az webapp deployment*') -eq 0)
    }

    Check 'Invoke-BallastDeploy: a forbidden setting refuses the app before any build (exit 1)' {
        Set-DeployFixtures
        Set-Az 'webapp config appsettings list' (New-Result -Output @('[{"name":"WEBSITE_RUN_FROM_PACKAGE","value":"1"},{"name":"WEBSITE_WARMUP_PATH","value":"/health/ready"}]'))
        $out = Invoke-DeployCaptured -Config (New-TestConfig) -Yes
        ($out.Result.ExitCode -eq 1) -and ((Measure-LogCount 'run dotnet publish*') -eq 0) -and ((Measure-LogCount 'az webapp deployment*') -eq 0)
    }

    Check 'Invoke-BallastDeploy: -WhatIf builds and prints the plan, deploys nothing and tags nothing (exit 0)' {
        Set-DeployFixtures
        $out = Invoke-DeployCaptured -Config (New-TestConfig) -WhatIf
        ($out.Result.ExitCode -eq 0) -and ((Measure-LogCount 'run dotnet publish*') -eq 1) -and ((Measure-LogCount 'az webapp deployment*') -eq 0) -and
            ((Measure-LogCount 'git push*') -eq 0) -and ($out.Text -match 'nothing deployed')
    }

    Check 'Invoke-BallastDeploy: answering no to the prompt deploys nothing (exit 0)' {
        Set-DeployFixtures
        $script:DeployAnswer = 'n'
        $out = Invoke-DeployCaptured -Config (New-TestConfig)
        ($out.Result.ExitCode -eq 0) -and ((Measure-LogCount 'az webapp deployment*') -eq 0) -and ((Measure-LogCount 'git push*') -eq 0)
    }

    Check 'Invoke-BallastDeploy: a deploy with -Yes goes through the zip, the proof and the tag (exit 0)' {
        Set-DeployFixtures
        $out = Invoke-DeployCaptured -Config (New-TestConfig) -Yes
        $tagged = Measure-LogCount 'git push origin refs/tags/deploy/api/*-1'
        ($out.Result.ExitCode -eq 0) -and ((Measure-LogCount 'az webapp deployment source config-zip*') -eq 1) -and ($tagged -eq 1) -and
            ([string]$script:TagMessages['deploy/api/' + (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd') + '-1'] -match "commit: $($script:Sha)") -and
            ($out.Result.Apps.Count -eq 1)
    }

    Check 'Invoke-BallastDeploy: the build folders are removed afterwards' {
        Set-DeployFixtures
        $null = Invoke-DeployCaptured -Config (New-TestConfig) -Yes
        $created = @($script:CreatedDirs)
        ($created.Count -eq 1) -and (-not (Test-Path -LiteralPath $created[0]))
    }

    Check 'Invoke-BallastDeploy: the subscription is selected before the first app call, and the default is put back' {
        Set-DeployFixtures
        $null = Invoke-DeployCaptured -Config (New-TestConfig) -Yes
        $select = Find-LogIndex 'az account set --subscription 11111111*'
        $firstAppCall = Find-LogIndex 'az webapp config appsettings list*'
        $restore = Find-LogIndex 'az account set --subscription 44444444*'
        ($select -ge 0) -and ($firstAppCall -gt $select) -and ($restore -gt $firstAppCall)
    }

    Check 'Invoke-BallastDeploy: a switch that is not proven is exit 3, with no tag' {
        Set-DeployFixtures
        $script:HealthAnswers = New-Object System.Collections.Queue
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:OldSha))
        $script:ReadyAnswers = New-Object System.Collections.Queue
        $script:ReadyAnswers.Enqueue(200)
        $out = Invoke-DeployCaptured -Config (New-TestConfig) -Yes -TimeoutSeconds 1
        ($out.Result.ExitCode -eq 3) -and ((Measure-LogCount 'git push*') -eq 0)
    }

    Check 'Invoke-BallastDeploy: a tag push that origin refuses every time is exit 4, with the push command printed' {
        Set-DeployFixtures
        $script:PushRefusals = 10
        $out = Invoke-DeployCaptured -Config (New-TestConfig) -Yes
        ($out.Result.ExitCode -eq 4) -and ($out.Text -match 'git push origin refs/tags/deploy/api/')
    }

    Check 'Invoke-BallastDeploy: -RefusePendingMigrations stops the app with exit 2 when a migration is pending' {
        Set-DeployFixtures -WithMigrations
        $script:ListAnswers = New-Object System.Collections.Queue
        $script:ListAnswers.Enqueue(@($script:PendingListing))
        $out = Invoke-DeployCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Yes -RefusePendingMigrations
        ($out.Result.ExitCode -eq 2) -and ((Measure-LogCount 'az webapp deployment*') -eq 0) -and ((Measure-LogCount 'bundle-apply') -eq 0)
    }

    Check 'Invoke-BallastDeploy: migrations declined at their prompt are exit 2, and nothing is deployed' {
        Set-DeployFixtures -WithMigrations
        $script:MigrationAnswer = 'n'
        $out = Invoke-DeployCaptured -Config (New-TestConfig -WithMigrations -WithGuard)
        ($out.Result.ExitCode -eq 2) -and ((Measure-LogCount 'az webapp deployment*') -eq 0)
    }

    Check 'Invoke-BallastDeploy: pending migrations are applied, then the app is deployed, in that order (exit 0)' {
        Set-DeployFixtures -WithMigrations
        $out = Invoke-DeployCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Yes
        $applied = Find-LogIndex 'bundle-apply'
        $zip = Find-LogIndex 'az webapp deployment source config-zip*'
        ($out.Result.ExitCode -eq 0) -and ($applied -ge 0) -and ($zip -gt $applied)
    }

    Check 'Invoke-BallastDeploy: a migration that fails stops before the zip deploy (exit 1)' {
        Set-DeployFixtures -WithMigrations
        $script:ApplyExit = 1
        $out = Invoke-DeployCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Yes
        ($out.Result.ExitCode -eq 1) -and ((Measure-LogCount 'az webapp deployment*') -eq 0)
    }

    Check 'Invoke-BallastDeploy: no setting value and no connection secret is printed by a full run' {
        Set-DeployFixtures -WithMigrations
        $out = Invoke-DeployCaptured -Config (New-TestConfig -WithMigrations -WithGuard) -Yes
        ($out.Result.ExitCode -eq 0) -and ($out.Text -notmatch 'sentinel-setting-value-77') -and ($out.Text -notmatch 'sentinel-pw-42')
    }

    Check 'Invoke-BallastDeploy: a tag for another app is refused as a rollback (exit 1)' {
        Set-DeployFixtures
        $script:GitRefs['deploy/map/20261021-1'] = $script:Sha
        $script:LocalTags['deploy/map/20261021-1'] = $script:Sha
        $out = Invoke-DeployCaptured -Config (New-TestConfig) -Ref 'deploy/map/20261021-1' -Yes
        ($out.Result.ExitCode -eq 1) -and ((Measure-LogCount 'az webapp deployment*') -eq 0)
    }

    Check 'Invoke-BallastDeploy: -WhatIf with two apps previews both, in the order given, and deploys neither' {
        Set-DeployFixtures
        $script:HealthAnswers = New-Object System.Collections.Queue
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:OldSha))
        $script:HealthAnswers.Enqueue((New-Health -Commit $script:OldSha))
        $script:ReadyAnswers = New-Object System.Collections.Queue
        $script:ReadyAnswers.Enqueue(200)
        $out = Invoke-DeployCaptured -Config (New-TestConfig -TwoApps) -App @('api', 'map') -WhatIf
        $apiAt = $out.Text.IndexOf('would deploy', [StringComparison]::Ordinal)
        $mapAt = $out.Text.IndexOf('would deploy', $apiAt + 1, [StringComparison]::Ordinal)
        ($out.Result.ExitCode -eq 0) -and ($apiAt -ge 0) -and ($mapAt -gt $apiAt) -and ((Measure-LogCount 'az webapp deployment*') -eq 0)
    }

    Check 'The module exports exactly the five public functions' {
        $names = @(Get-Command -Module 'Ballast.Deploy' | ForEach-Object { $_.Name } | Sort-Object)
        (($names -join ',') -eq 'Hide-BallastSecret,Invoke-BallastDeploy,Invoke-BallastMigrations,Read-BallastConfig,Wait-BallastAppSwitched')
    }
}
finally {
    Remove-Item -LiteralPath $script:Root -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "$($script:Passed) passed, $($script:Failed) failed"
if ($script:Failed -gt 0) { exit 1 }
exit 0

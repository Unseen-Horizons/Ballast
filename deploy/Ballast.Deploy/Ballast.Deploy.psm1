# Copyright (c) 2026 Unseen Horizons
# This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
# distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

# Ballast.Deploy: builds a pushed commit, migrates an Azure SQL database from an EF bundle, deploys the commit as a zip,
# proves the switch, and tags it. An app describes its placement in its own apps.psd1 (docs/Deploy.md). This module names
# no app. The exit codes are frozen; each public entry point returns an object with ExitCode and Message.

Set-StrictMode -Version Latest

$script:Invariant = [System.Globalization.CultureInfo]::InvariantCulture

$script:DefaultSecretPatterns = @(
    '(?i)(?<=bearer\s+)[A-Za-z0-9._~+/-]+=*',
    '(?i)(?<=AccountKey=)[^;\s]+',
    '(?i)(?<=SharedAccessSignature=)[^;\s]+',
    '(?i)(?<=\bpassword\s*=\s*)[^;\s]+',
    '(?i)(?<=\bpwd\s*=\s*)[^;\s]+'
)

$script:OptionDefaults = @{
    ForbiddenSettings     = @('WEBSITE_WARMUP_PATH', 'WEBSITE_WARMUP_STATUSES')
    SecretPatterns        = $script:DefaultSecretPatterns
    TagPushAttempts       = 3
    TagPushDelaySeconds   = 5
    TagDateFormat         = 'yyyy-MM-dd'
    RuntimeIdentifier     = 'linux-x64'
    ListRetries           = 6
    MinimumSdkMajor       = 8
    ListRetryDelaySeconds = 20
}
$script:AppFields = @('Subscription', 'ResourceGroup', 'WebApp', 'HealthUrl', 'Project', 'LegacyBuildTimeProperty', 'Migrations', 'RequireCaseInsensitiveCollation')
$script:MigrationFields = @('DbContext', 'ConnectionSetting', 'SqlServer', 'Guard', 'Environment', 'ThrowawayVariables', 'ThrowawayDirectories')
$script:TagDateFormats = @('yyyyMMdd', 'yyyy-MM-dd')

$script:SecretPatterns = $script:DefaultSecretPatterns
$script:SecretValues = [System.Collections.Generic.List[string]]::new()

# --- Runners. Every az, git, dotnet, HTTP, IP and prompt call goes through one of these. Tests replace them. ----------

# az: returns { ExitCode, Output, ErrorText }. Output is captured, so az's progress on stderr is shown only when a call fails.
$script:AzRunner = {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $all = @(& az @Arguments 2>&1)
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{
        ExitCode  = $code
        Output    = @($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" })
        ErrorText = (@($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n")
    }
}

$script:GitRunner = {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $all = @(& git @Arguments 2>&1)
        $code = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{
        ExitCode  = $code
        Output    = @($all | Where-Object { $_ -isnot [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" })
        ErrorText = (@($all | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | ForEach-Object { "$_" }) -join "`n")
    }
}

# dotnet and the migration bundle. Environment values are set for the child only. With -Stream, each line is written
# to the host as it arrives (redacted); the lines are returned either way.
$script:ProcessRunner = {
    param([string]$FilePath, [string[]]$Arguments, [hashtable]$Environment, [string]$WorkingDirectory, [bool]$Stream)
    $saved = @{}
    foreach ($key in $Environment.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, [string]$Environment[$key])
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    Push-Location -LiteralPath $WorkingDirectory
    try {
        $lines = @(& $FilePath @Arguments 2>&1 | ForEach-Object {
                $text = "$_"
                if ($Stream) { Write-Host (Hide-BallastSecret -Text $text) }
                $text
            })
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
        $ErrorActionPreference = $previous
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
    }
    return [pscustomobject]@{ ExitCode = $code; Output = $lines }
}

# Returns the parsed body of one GET. Status 0 means no answer at all (DNS, TLS, timeout).
$script:HealthRunner = {
    param([string]$Url)
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 30
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = [string]$response.Content }
    }
    catch {
        $failed = $_.Exception.Response
        if ($null -ne $failed) { return [pscustomobject]@{ Status = [int]$failed.StatusCode; Body = '' } }
        return [pscustomobject]@{ Status = 0; Body = '' }
    }
}

$script:IpRunner = {
    param([string]$Url)
    return Invoke-RestMethod -Uri $Url -TimeoutSec 15
}

# The y/N question. With input redirected nobody can answer, so it fails rather than reading a closed stdin as "no".
$script:PromptRunner = {
    param([string]$Question)
    if ([Console]::IsInputRedirected -or $env:CI -or $env:GITHUB_ACTIONS) {
        throw "$Question Nothing can answer it (input is redirected, or CI is set). Pass -Yes to skip the prompt."
    }
    return [string](Read-Host $Question)
}

# --- Output and secrets -----------------------------------------------------------------------------------------------

function Write-BallastStep {
    param([string]$Text)
    Write-Host ('==> ' + (Hide-BallastSecret -Text $Text)) -ForegroundColor Cyan
}

function Write-BallastNote {
    param([string]$Text)
    Write-Host ('    ' + (Hide-BallastSecret -Text $Text)) -ForegroundColor DarkGray
}

function Write-BallastWarning {
    param([string]$Text)
    Write-Host ('!!! ' + (Hide-BallastSecret -Text $Text)) -ForegroundColor Yellow
}

<#
.SYNOPSIS
    Redacts secrets from text before it is logged or thrown.

.DESCRIPTION
    Each value registered with the run is replaced first, then every regular expression in the list. The default list
    covers bearer tokens, AccountKey=, SharedAccessSignature=, password= and pwd=. The list comes from the config's
    SecretPatterns option during a run. Pass -Patterns to use another list for this call only. Each pattern's match is
    replaced with <redacted>, so write a lookbehind to keep the key name visible.
#>
function Hide-BallastSecret {
    [CmdletBinding()]
    param(
        [AllowNull()][AllowEmptyString()][string]$Text,
        [string[]]$Patterns
    )

    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    $safe = $Text
    foreach ($value in $script:SecretValues) { $safe = $safe.Replace($value, '<redacted>') }

    $list = $script:SecretPatterns
    if ($PSBoundParameters.ContainsKey('Patterns')) { $list = $Patterns }
    foreach ($pattern in @($list)) { $safe = [regex]::Replace($safe, $pattern, '<redacted>') }
    return $safe
}

# Registers a value (a connection string, a password, a generated key) so that it is redacted by value wherever it appears.
function Register-BallastSecret {
    param([AllowNull()][AllowEmptyString()][string]$Value)
    if ($Value -and $Value.Length -ge 4 -and -not $script:SecretValues.Contains($Value)) { $script:SecretValues.Add($Value) }
}

function New-BallastSigningKey {
    return [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
}

# Ends a run with a specific exit code. The catch in the public entry point turns it into that code.
function Stop-BallastRun {
    param([Parameter(Mandatory)][int]$Code, [Parameter(Mandatory)][string]$Message)
    $exception = [System.Exception]::new($Message)
    $exception.Data['BallastExit'] = $Code
    throw $exception
}

function Get-BallastTail {
    param([AllowNull()][object[]]$Lines, [int]$Count = 15)
    $all = @($Lines | Where-Object { $null -ne $_ -and "$_".Trim() } | ForEach-Object { "$_" })
    if ($all.Count -eq 0) { return '(no output)' }
    $start = [Math]::Max(0, $all.Count - $Count)
    return (Hide-BallastSecret -Text (($all[$start..($all.Count - 1)]) -join "`n"))
}

function Get-BallastProperty {
    param([AllowNull()][object]$Object, [Parameter(Mandatory)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

# --- Runner wrappers. These are the only places that call az, git, dotnet, HTTP, the IP service or the prompt. --------

<#
    Runs az through $script:AzRunner. Throws on a non-zero exit, with az's own stderr, unless -AllowFailure is given; then the
    result is returned as it is. Arguments go as one array, so PowerShell never binds an -o to a common parameter.
#>
function Invoke-Az {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Arguments, [switch]$AllowFailure)

    $result = & $script:AzRunner -Arguments $Arguments
    if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
        $detail = [string]$result.ErrorText
        if (-not $detail.Trim()) { $detail = (@($result.Output) -join "`n") }
        throw "az $(Hide-BallastSecret -Text ($Arguments -join ' ')) failed (exit $($result.ExitCode)):`n$(Hide-BallastSecret -Text $detail)"
    }
    return $result
}

# Runs git through $script:GitRunner with -C <RepoPath>. Same rules as Invoke-Az.
function Invoke-Git {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$Arguments, [string]$RepoPath = '.', [switch]$AllowFailure)

    $full = @('-C', $RepoPath) + $Arguments
    $result = & $script:GitRunner -Arguments $full
    if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
        $detail = [string]$result.ErrorText
        if (-not $detail.Trim()) { $detail = (@($result.Output) -join "`n") }
        throw "git $(Hide-BallastSecret -Text ($Arguments -join ' ')) failed (exit $($result.ExitCode)):`n$(Hide-BallastSecret -Text $detail)"
    }
    return $result
}

function Invoke-BallastProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$Arguments = @(),
        [hashtable]$Environment = @{},
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [switch]$Stream
    )
    return (& $script:ProcessRunner -FilePath $FilePath -Arguments $Arguments -Environment $Environment -WorkingDirectory $WorkingDirectory -Stream ([bool]$Stream))
}

function Invoke-BallastHealth {
    param([Parameter(Mandatory)][string]$Url)
    return (& $script:HealthRunner -Url $Url)
}

# Returns this machine's public IPv4 address. Tries ipify, then checkip.amazonaws.com; a failure of both is an error.
function Get-BallastPublicIPv4 {
    $found = ''
    try { $found = [string](Get-BallastProperty -Object (& $script:IpRunner -Url 'https://api.ipify.org?format=json') -Name 'ip') }
    catch { Write-BallastNote 'ipify did not answer; trying checkip.amazonaws.com' }
    if (-not $found) {
        try { $found = ([string](& $script:IpRunner -Url 'https://checkip.amazonaws.com')).Trim() }
        catch { $found = '' }
    }
    $address = $null
    $parsed = [System.Net.IPAddress]::TryParse($found, [ref]$address)
    if (-not ($parsed -and $address.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork)) {
        throw "Could not determine this machine's public IPv4 address (ipify and checkip.amazonaws.com both failed)."
    }
    return $address.ToString()
}

# Asks the user y/N. -Yes answers yes without asking. Anything but y or yes is no.
function Confirm-BallastOwner {
    param([Parameter(Mandatory)][string]$Question, [switch]$Yes)
    if ($Yes) {
        Write-BallastNote "$Question (-Yes)"
        return $true
    }
    $answer = [string](& $script:PromptRunner -Question $Question)
    return ($answer -match '^\s*(y|yes)\s*$')
}

# --- Configuration ----------------------------------------------------------------------------------------------------

<#
.SYNOPSIS
    Reads an apps.psd1 config file and returns it normalised and validated.

.DESCRIPTION
    Import-PowerShellDataFile reads the file, which must hold data only. Throws with the reason when a key is missing,
    unknown or malformed. An unknown Options key is refused, so a typo does not silently keep a default.
#>
function Read-BallastConfig {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "No config file at '$Path'." }
    $data = Import-PowerShellDataFile -LiteralPath $Path
    return (Resolve-BallastConfig -Config $data)
}

function Resolve-BallastConfig {
    param([Parameter(Mandatory)][object]$Config)

    if ($Config -isnot [hashtable]) { throw 'The config must be a hashtable, as a .psd1 data file gives.' }

    $options = @{}
    foreach ($name in $script:OptionDefaults.Keys) { $options[$name] = $script:OptionDefaults[$name] }
    if ($Config.ContainsKey('Options')) {
        if ($Config['Options'] -isnot [hashtable]) { throw 'Options must be a hashtable.' }
        foreach ($name in $Config['Options'].Keys) {
            if (-not $script:OptionDefaults.ContainsKey($name)) {
                throw "Unknown option '$name'. The options are: $((@($script:OptionDefaults.Keys) | Sort-Object) -join ', ')."
            }
            $options[$name] = $Config['Options'][$name]
        }
    }

    $options.ForbiddenSettings = @($options.ForbiddenSettings | ForEach-Object { [string]$_ })
    foreach ($name in $options.ForbiddenSettings) {
        if ($name -cnotmatch '^[A-Za-z0-9_:.-]+$') { throw "ForbiddenSettings entry '$name' is not an app setting name." }
    }
    $options.SecretPatterns = @($options.SecretPatterns | ForEach-Object { [string]$_ })
    foreach ($pattern in $options.SecretPatterns) {
        try { $null = [regex]::new($pattern) }
        catch { throw "SecretPatterns entry '$pattern' is not a valid regular expression." }
    }
    $options.TagPushAttempts = Get-BallastIntOption -Name 'TagPushAttempts' -Value $options.TagPushAttempts -Minimum 1
    $options.TagPushDelaySeconds = Get-BallastIntOption -Name 'TagPushDelaySeconds' -Value $options.TagPushDelaySeconds -Minimum 0
    $options.ListRetries = Get-BallastIntOption -Name 'ListRetries' -Value $options.ListRetries -Minimum 0
    $options.ListRetryDelaySeconds = Get-BallastIntOption -Name 'ListRetryDelaySeconds' -Value $options.ListRetryDelaySeconds -Minimum 0
    $options.MinimumSdkMajor = Get-BallastIntOption -Name 'MinimumSdkMajor' -Value $options.MinimumSdkMajor -Minimum 1
    if ($script:TagDateFormats -cnotcontains [string]$options.TagDateFormat) {
        throw "TagDateFormat must be one of: $($script:TagDateFormats -join ', ')."
    }
    if ([string]$options.RuntimeIdentifier -cnotmatch '^[a-z0-9-]+$') { throw "RuntimeIdentifier '$($options.RuntimeIdentifier)' is not a runtime identifier such as linux-x64." }

    if (-not $Config.ContainsKey('Apps') -or $Config['Apps'] -isnot [hashtable] -or $Config['Apps'].Count -eq 0) {
        throw 'The config needs an Apps hashtable with at least one app in it.'
    }

    $apps = @{}
    foreach ($name in $Config['Apps'].Keys) {
        $appName = [string]$name
        if ($appName -cnotmatch '^[a-z][a-z0-9-]{0,39}$') {
            throw "The app name '$appName' must be lower case letters, digits and hyphens, starting with a letter. It names the deploy tags."
        }
        $raw = $Config['Apps'][$name]
        if ($raw -isnot [hashtable]) { throw "App '$appName' must be a hashtable." }
        foreach ($field in $raw.Keys) {
            if ($script:AppFields -notcontains [string]$field) { throw "App '$appName' has an unknown key '$field'." }
        }
        foreach ($required in @('Subscription', 'ResourceGroup', 'WebApp', 'HealthUrl', 'Project')) {
            if ([string]::IsNullOrWhiteSpace([string]$raw[$required])) { throw "App '$appName' needs $required." }
        }

        $healthUrl = ([string]$raw['HealthUrl']).Trim()
        if ($healthUrl -cnotmatch '^https://[^/\s]+/?$') {
            throw "App '$appName' HealthUrl must be an https address with no path, such as https://api.example.net."
        }
        $project = ([string]$raw['Project']).Trim()
        if ([System.IO.Path]::IsPathRooted($project) -or $project -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "App '$appName' Project must be a path inside the repository."
        }
        $legacy = [string]$raw['LegacyBuildTimeProperty']
        if ($legacy -and $legacy -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            throw "App '$appName' LegacyBuildTimeProperty must be an MSBuild property name."
        }
        $migrations = $null
        if ($null -ne $raw['Migrations']) { $migrations = Resolve-BallastMigrationsConfig -Name $appName -Value $raw['Migrations'] }

        $requireCollation = $false
        if ($null -ne $raw['RequireCaseInsensitiveCollation']) {
            if ($raw['RequireCaseInsensitiveCollation'] -isnot [bool]) { throw "App '$appName' RequireCaseInsensitiveCollation must be `$true or `$false." }
            $requireCollation = [bool]$raw['RequireCaseInsensitiveCollation']
        }
        if ($requireCollation -and ($null -eq $migrations -or -not $migrations.SqlServer)) {
            throw "App '$appName' sets RequireCaseInsensitiveCollation, which reads the collation of a Migrations database. It needs Migrations with a SqlServer."
        }

        $apps[$appName] = @{
            Subscription                    = ([string]$raw['Subscription']).Trim()
            ResourceGroup                   = ([string]$raw['ResourceGroup']).Trim()
            WebApp                          = ([string]$raw['WebApp']).Trim()
            HealthUrl                       = $healthUrl.TrimEnd('/')
            Project                         = $project
            LegacyBuildTimeProperty         = $legacy
            Migrations                      = $migrations
            RequireCaseInsensitiveCollation = $requireCollation
        }
    }
    return @{ Options = $options; Apps = $apps }
}

function Get-BallastIntOption {
    param([string]$Name, [AllowNull()][object]$Value, [int]$Minimum)
    $number = 0
    if (-not [int]::TryParse([string]$Value, [ref]$number) -or $number -lt $Minimum) {
        throw "Option $Name must be a whole number, $Minimum or more."
    }
    return $number
}

function Resolve-BallastMigrationsConfig {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][object]$Value)

    if ($Value -isnot [hashtable]) { throw "App '$Name' Migrations must be a hashtable." }
    foreach ($field in $Value.Keys) {
        if ($script:MigrationFields -notcontains [string]$field) { throw "App '$Name' Migrations has an unknown key '$field'." }
    }

    $context = ([string]$Value['DbContext']).Trim()
    if ($context -cnotmatch '^[A-Za-z_][A-Za-z0-9_.]*$') {
        throw "App '$Name' Migrations needs DbContext: the EF context class name, such as AppDbContext."
    }
    $connection = 'ConnectionStrings__DefaultConnection'
    if ($Value['ConnectionSetting']) { $connection = [string]$Value['ConnectionSetting'] }
    if ($connection -cnotmatch '^[A-Za-z_][A-Za-z0-9_:.-]*$') { throw "App '$Name' Migrations ConnectionSetting is not an app setting name." }

    $sqlServer = ''
    if ($Value['SqlServer']) {
        $sqlServer = ([string]$Value['SqlServer']).Trim()
        if ($sqlServer -cnotmatch '^[A-Za-z0-9-]{1,63}$') { throw "App '$Name' Migrations SqlServer must be a server name, without the domain." }
    }

    $guard = $null
    if ($null -ne $Value['Guard']) {
        $raw = $Value['Guard']
        if ($raw -isnot [hashtable]) { throw "App '$Name' Migrations Guard must be a hashtable with Project and Filter." }
        $guardProject = ([string]$raw['Project']).Trim()
        $guardFilter = ([string]$raw['Filter']).Trim()
        if (-not $guardProject -or -not $guardFilter) { throw "App '$Name' Migrations Guard needs Project and Filter." }
        if ([System.IO.Path]::IsPathRooted($guardProject) -or $guardProject -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "App '$Name' Migrations Guard Project must be a path inside the repository."
        }
        $guard = @{ Project = $guardProject; Filter = $guardFilter }
    }

    $childEnvironment = @{}
    if ($null -ne $Value['Environment']) {
        if ($Value['Environment'] -isnot [hashtable]) { throw "App '$Name' Migrations Environment must be a hashtable." }
        foreach ($key in $Value['Environment'].Keys) {
            if ([string]$key -cnotmatch '^[A-Za-z_][A-Za-z0-9_:.-]*$') { throw "App '$Name' Migrations Environment key '$key' is not a variable name." }
            $childEnvironment[[string]$key] = [string]$Value['Environment'][$key]
        }
    }

    $throwaway = @()
    if ($null -ne $Value['ThrowawayVariables']) {
        foreach ($variable in @($Value['ThrowawayVariables'])) {
            if ([string]$variable -cnotmatch '^[A-Za-z_][A-Za-z0-9_:.-]*$') { throw "App '$Name' Migrations ThrowawayVariables entry '$variable' is not a variable name." }
            $throwaway += [string]$variable
        }
    }

    $directories = @{}
    if ($null -ne $Value['ThrowawayDirectories']) {
        if ($Value['ThrowawayDirectories'] -isnot [hashtable]) { throw "App '$Name' Migrations ThrowawayDirectories must be a hashtable of variable name to marker file name." }
        foreach ($variable in $Value['ThrowawayDirectories'].Keys) {
            $variableName = [string]$variable
            $marker = [string]$Value['ThrowawayDirectories'][$variable]
            if ($variableName -cnotmatch '^[A-Za-z_][A-Za-z0-9_:.-]*$') { throw "App '$Name' Migrations ThrowawayDirectories name '$variableName' is not a variable name." }
            if ($marker -cnotmatch '^\.?[A-Za-z0-9][A-Za-z0-9._-]{0,99}$') { throw "App '$Name' Migrations ThrowawayDirectories marker '$marker' must be a plain file name, such as .marker-file." }
            $directories[$variableName] = $marker
        }
    }
    return @{
        DbContext            = $context
        ConnectionSetting   = $connection
        SqlServer           = $sqlServer
        Guard               = $guard
        Environment         = $childEnvironment
        ThrowawayVariables  = @($throwaway)
        ThrowawayDirectories = $directories
    }
}

function Get-BallastAppConfig {
    param([Parameter(Mandatory)][hashtable]$Config, [Parameter(Mandatory)][string]$App)
    if (-not $Config.Apps.ContainsKey($App)) {
        throw "No app '$App' in the config. The apps are: $((@($Config.Apps.Keys) | Sort-Object) -join ', ')."
    }
    return $Config.Apps[$App]
}

# --- Subscription and app settings ------------------------------------------------------------------------------------

# Picks the one enabled subscription called Name, or whose id is Name, from `az account list --all`. A disabled subscription
# can share the live one's name, so only an enabled match counts. Takes the parsed list, so it runs without az.
function Select-BallastSubscriptionId {
    param([AllowNull()][AllowEmptyCollection()][object[]]$Accounts, [Parameter(Mandatory)][string]$Name)

    $found = @($Accounts | Where-Object {
            $null -ne $_ -and (([string](Get-BallastProperty -Object $_ -Name 'name')) -eq $Name -or ([string](Get-BallastProperty -Object $_ -Name 'id')) -eq $Name)
        })
    $enabled = @($found | Where-Object { ([string](Get-BallastProperty -Object $_ -Name 'state')) -eq 'Enabled' })
    if ($enabled.Count -eq 1) { return [string](Get-BallastProperty -Object $enabled[0] -Name 'id') }

    $described = (@($found | ForEach-Object {
                "$(Get-BallastProperty -Object $_ -Name 'name') ($(Get-BallastProperty -Object $_ -Name 'id'), $(Get-BallastProperty -Object $_ -Name 'state'))"
            }) -join '; ')
    if ($enabled.Count -gt 1) { throw "More than one enabled subscription is called '$Name': $described. Pass its id instead." }
    if ($found.Count -eq 0) { throw "No subscription called or with id '$Name' is in this az profile. Sign in with the account that owns it: az login." }
    throw "The subscription '$Name' is not enabled: $described."
}

# Makes the named subscription the az default and returns the id that was the default before. The caller puts it back.
function Use-BallastSubscription {
    param([Parameter(Mandatory)][string]$Subscription)

    $shown = (@((Invoke-Az -Arguments @('account', 'show', '-o', 'json')).Output) -join "`n")
    if (-not $shown.Trim()) { throw 'az account show gave no output. Sign in with az login.' }
    $previous = [string](Get-BallastProperty -Object ($shown | ConvertFrom-Json) -Name 'id')

    $listed = (@((Invoke-Az -Arguments @('account', 'list', '--all', '-o', 'json')).Output) -join "`n")
    $accounts = @()
    if ($listed.Trim()) { $accounts = @($listed | ConvertFrom-Json) }
    $id = Select-BallastSubscriptionId -Accounts $accounts -Name $Subscription

    $null = Invoke-Az -Arguments @('account', 'set', '--subscription', $id, '-o', 'none')
    Write-BallastNote "Subscription: $Subscription ($id)"
    return $previous
}

function Restore-BallastSubscription {
    param([string]$Id)
    if ([string]::IsNullOrWhiteSpace($Id)) { return }
    try {
        $null = Invoke-Az -Arguments @('account', 'set', '--subscription', $Id, '-o', 'none')
        Write-BallastNote "az default subscription restored: $Id"
    }
    catch {
        Write-BallastWarning "Could not restore the az default subscription to $Id. Set it by hand: az account set --subscription $Id"
    }
}

# Returns the app's settings as a case-insensitive hashtable. Values stay in memory; nothing here prints them.
function Get-BallastAppSettings {
    param([Parameter(Mandatory)][string]$ResourceGroup, [Parameter(Mandatory)][string]$WebApp)

    $values = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    $json = (@((Invoke-Az -Arguments @('webapp', 'config', 'appsettings', 'list', '-g', $ResourceGroup, '-n', $WebApp, '-o', 'json')).Output) -join "`n")
    if (-not $json.Trim()) { return $values }
    foreach ($setting in @($json | ConvertFrom-Json)) {
        $name = [string](Get-BallastProperty -Object $setting -Name 'name')
        if ($name) { $values[$name] = [string](Get-BallastProperty -Object $setting -Name 'value') }
    }
    return $values
}

function New-BallastLine {
    param([bool]$Refused, [string]$Text)
    return [pscustomobject]@{ Refused = $Refused; Text = $Text }
}

# Refuses an app that does not run from its package. Returns Ok and the lines to show. Only names and pass or fail are
# in the lines, never a value.
function Test-BallastRunFromPackage {
    param([Parameter(Mandatory)][hashtable]$Settings, [Parameter(Mandatory)][string]$WebApp)

    $ok = $true
    $lines = @()
    if ($Settings.ContainsKey('WEBSITE_RUN_FROM_PACKAGE') -and $Settings['WEBSITE_RUN_FROM_PACKAGE'] -ceq '1') {
        $lines += New-BallastLine -Refused $false -Text "$WebApp  WEBSITE_RUN_FROM_PACKAGE  ok"
    }
    else {
        $lines += New-BallastLine -Refused $true -Text "$WebApp  WEBSITE_RUN_FROM_PACKAGE  REFUSED: must be exactly 1."
        $ok = $false
    }
    foreach ($name in @('SCM_DO_BUILD_DURING_DEPLOYMENT', 'ENABLE_ORYX_BUILD')) {
        if ($Settings.ContainsKey($name) -and $Settings[$name] -ieq 'true') {
            $lines += New-BallastLine -Refused $true -Text "$WebApp  $name  REFUSED: must not be true (the server would build the zip again)."
            $ok = $false
        }
        else {
            $lines += New-BallastLine -Refused $false -Text "$WebApp  $name  ok"
        }
    }
    return [pscustomobject]@{ Ok = $ok; Lines = @($lines) }
}

# Refuses an app that has any forbidden setting, present at all (even empty). The remedy named is the removal only.
function Test-BallastForbiddenSettings {
    param(
        [Parameter(Mandatory)][hashtable]$Settings,
        [AllowNull()][string[]]$Forbidden,
        [Parameter(Mandatory)][string]$ResourceGroup,
        [Parameter(Mandatory)][string]$WebApp
    )

    $ok = $true
    $lines = @()
    foreach ($name in @($Forbidden)) {
        if ($Settings.ContainsKey($name)) {
            $ok = $false
            $lines += New-BallastLine -Refused $true -Text "$name is set on $WebApp, and the deploy refuses an app that has it. Remove it, then deploy again: az webapp config appsettings delete -g $ResourceGroup -n $WebApp --setting-names $name"
        }
    }
    if ($ok) { $lines += New-BallastLine -Refused $false -Text "$WebApp  no forbidden settings" }
    return [pscustomobject]@{ Ok = $ok; Lines = @($lines) }
}

function Write-BallastLines {
    param([Parameter(Mandatory)][object]$Result)
    foreach ($line in @($Result.Lines)) {
        if ($line.Refused) { Write-BallastWarning $line.Text } else { Write-BallastNote $line.Text }
    }
}

# --- Git and the build --------------------------------------------------------------------------------------------------

<#
    Resolves -Ref to the commit a deploy builds. A deploy tag names one app and one commit: it may only deploy that app, and
    never several. A rollback is a deploy of an older deploy tag.
#>
function Resolve-BallastRef {
    param([string]$Ref = 'HEAD', [Parameter(Mandatory)][string[]]$App, [string]$RepoPath = '.')

    $commit = Invoke-Git -RepoPath $RepoPath -Arguments @('rev-parse', '--verify', '--quiet', "$Ref^{commit}") -AllowFailure
    if ($commit.ExitCode -ne 0 -or @($commit.Output).Count -eq 0) {
        throw "'$Ref' does not name a commit in this repository. Fetch first, or check the spelling."
    }
    $sha = ([string]$commit.Output[0]).Trim()
    $subject = (@((Invoke-Git -RepoPath $RepoPath -Arguments @('log', '-1', '--format=%s', $sha)).Output) -join '')

    $tagName = $null
    $tagApp = $null
    $isTag = Invoke-Git -RepoPath $RepoPath -Arguments @('rev-parse', '--verify', '--quiet', "refs/tags/$Ref") -AllowFailure
    if ($isTag.ExitCode -eq 0 -and $Ref -cmatch '^deploy/(?<app>[a-z][a-z0-9-]*)/') {
        $tagApp = $Matches['app']
        $tagName = $Ref
        if ($App.Count -ne 1) { throw "$Ref is a deploy tag for $tagApp. Name that one app, not several." }
        if ($tagApp -ne $App[0]) {
            throw "$Ref is a deploy tag for $tagApp, but the app being deployed is $($App[0]). A tag only deploys the app it was made for."
        }
    }
    return [pscustomobject]@{ Ref = $Ref; Sha = $sha; Subject = $subject; TagName = $tagName; TagApp = $tagApp }
}

# A deploy builds a commit from a worktree, so the commit must already be on origin. Uncommitted changes only warn: the build
# never reads the checkout.
function Assert-BallastDeployableCommit {
    param([Parameter(Mandatory)][string]$Sha, [string]$RepoPath = '.')

    $remote = Invoke-Git -RepoPath $RepoPath -Arguments @('for-each-ref', '--format=%(refname)', "--contains=$Sha", 'refs/remotes/origin/')
    $onOrigin = @($remote.Output | Where-Object { $_ -and $_ -ne 'refs/remotes/origin/HEAD' })
    if ($onOrigin.Count -eq 0) {
        throw "Commit $($Sha.Substring(0, 8)) is not on origin. Push it, then deploy it: this module only builds pushed commits."
    }

    $dirty = Invoke-Git -RepoPath $RepoPath -Arguments @('status', '--porcelain')
    if (@($dirty.Output).Count -gt 0) {
        Write-BallastWarning "$(@($dirty.Output).Count) uncommitted path(s) in this checkout are not part of this deploy. The build comes from a clean worktree of $($Sha.Substring(0, 8))."
    }
}

# A clean worktree of the commit. The root is per run; a root left by a run that died is cleared first, so git does not refuse.
function New-BallastBuildWorktree {
    param([Parameter(Mandatory)][string]$Sha, [Parameter(Mandatory)][string]$Root, [string]$RepoPath = '.')

    $null = Invoke-Git -RepoPath $RepoPath -Arguments @('worktree', 'prune') -AllowFailure
    if (Test-Path -LiteralPath $Root) { Remove-BallastBuildWorktree -Root $Root -RepoPath $RepoPath }
    $null = New-Item -ItemType Directory -Force -Path $Root
    $dir = Join-Path $Root 'src'
    $null = Invoke-Git -RepoPath $RepoPath -Arguments @('worktree', 'add', '--detach', $dir, $Sha)
    return $dir
}

function Remove-BallastBuildWorktree {
    param([Parameter(Mandatory)][string]$Root, [string]$RepoPath = '.')

    $dir = Join-Path $Root 'src'
    if (Test-Path -LiteralPath $dir) {
        $null = Invoke-Git -RepoPath $RepoPath -Arguments @('worktree', 'remove', '--force', $dir) -AllowFailure
    }
    if (Test-Path -LiteralPath $Root) { Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue }
    $null = Invoke-Git -RepoPath $RepoPath -Arguments @('worktree', 'prune') -AllowFailure
    if (Test-Path -LiteralPath $Root) { Write-BallastWarning "Could not delete $Root (a file is in use). Delete it by hand." }
}

# The build stamp must be exactly yyyy-MM-ddTHH:mm:ssZ. Anything else is refused, never rounded.
function Test-BallastBuildTime {
    param([AllowNull()][string]$Value)
    if ($null -eq $Value -or $Value -cnotmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$') { return $false }
    $parsed = [DateTime]::MinValue
    return [DateTime]::TryParseExact($Value, "yyyy-MM-dd'T'HH:mm:ss'Z'", $script:Invariant, [System.Globalization.DateTimeStyles]::None, [ref]$parsed)
}

<#
    dotnet publish of one app from the build worktree, with the commit and the build time stamped in. The output is wiped
    first: a copy over an old folder keeps stale assemblies. The stamp property is BallastBuildTimeUtc. A LegacyBuildTimeProperty
    from the config gets the same value, so a rollback to an older build that reads that name still gets its time.
#>
function Invoke-BallastPublish {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$WorktreeDir,
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$OutDir,
        [Parameter(Mandatory)][string]$Sha,
        [Parameter(Mandatory)][string]$BuildTimeUtc,
        [string]$RuntimeIdentifier = 'linux-x64',
        [string]$LegacyBuildTimeProperty = ''
    )

    if ($Sha -cnotmatch '^[0-9a-f]{40}$') { throw 'The commit to build must be a full lowercase sha.' }
    if (-not (Test-BallastBuildTime -Value $BuildTimeUtc)) {
        throw "The build time '$BuildTimeUtc' is not exactly yyyy-MM-ddTHH:mm:ssZ, so nothing was built."
    }
    if ($LegacyBuildTimeProperty -and $LegacyBuildTimeProperty -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "LegacyBuildTimeProperty '$LegacyBuildTimeProperty' is not an MSBuild property name."
    }

    if (Test-Path -LiteralPath $OutDir) { Remove-Item -LiteralPath $OutDir -Recurse -Force }
    $projectPath = Join-Path $WorktreeDir $Project
    $arguments = @(
        'publish', $projectPath, '-c', 'Release', '-r', $RuntimeIdentifier, '--self-contained', 'false',
        '-o', $OutDir, '--nologo', "-p:SourceRevisionId=$Sha", "-p:BallastBuildTimeUtc=$BuildTimeUtc"
    )
    if ($LegacyBuildTimeProperty) { $arguments += "-p:$LegacyBuildTimeProperty=$BuildTimeUtc" }

    Write-BallastNote "dotnet publish $Project at $($Sha.Substring(0, 8))"
    $run = Invoke-BallastProcess -FilePath 'dotnet' -Arguments $arguments -WorkingDirectory $WorktreeDir -Stream
    if ($run.ExitCode -ne 0) { throw "Publish failed for $Project (exit $($run.ExitCode)).`n$(Get-BallastTail -Lines $run.Output)" }
}

# The EF bundle that applies this commit's migrations. Built from the worktree, so it matches the code being deployed.
function New-BallastMigrationBundle {
    param(
        [Parameter(Mandatory)][string]$WorktreeDir,
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$Context,
        [Parameter(Mandatory)][string]$OutDir,
        [hashtable]$Environment = @{}
    )

    $null = New-Item -ItemType Directory -Force -Path $OutDir
    $exe = 'efbundle'
    if ($IsWindows) { $exe = 'efbundle.exe' }
    $path = Join-Path $OutDir $exe
    $projectPath = Join-Path $WorktreeDir $Project
    $run = Invoke-BallastProcess -FilePath 'dotnet' -WorkingDirectory $WorktreeDir -Environment $Environment -Arguments @(
        'ef', 'migrations', 'bundle', '--project', $projectPath, '--startup-project', $projectPath, '--context', $Context,
        '--configuration', 'Release', '--force', '--output', $path
    )
    if ($run.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "dotnet ef migrations bundle failed.`n$(Get-BallastTail -Lines $run.Output)"
    }
    return $path
}

<#
    Zips a directory for the deployment, with forward-slash entry names. Windows separators in entry names make the host
    unpack files it cannot name, so every separator is normalised here.
#>
function New-BallastDeploymentZip {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$SourceDir, [Parameter(Mandatory)][string]$ZipPath)

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }

    $root = (Resolve-Path -LiteralPath $SourceDir).Path.TrimEnd([char]92, [char]47)
    $zip = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    $count = 0
    try {
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
            $entry = $file.FullName.Substring($root.Length + 1).Replace([char]92, [char]47)
            $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
            $count++
        }
    }
    finally { $zip.Dispose() }
    $mb = [math]::Round((Get-Item -LiteralPath $ZipPath).Length / 1MB, 1)
    Write-BallastNote "$count files -> $(Split-Path -Leaf $ZipPath) ($mb MB)"
    return $count
}

# The zip deploy. The stopwatch starts at the upload, so the switch time is measured from the same moment.
function Invoke-BallastZipDeploy {
    param([Parameter(Mandatory)][string]$ResourceGroup, [Parameter(Mandatory)][string]$WebApp, [Parameter(Mandatory)][string]$ZipPath)

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $result = Invoke-Az -AllowFailure -Arguments @('webapp', 'deployment', 'source', 'config-zip', '-g', $ResourceGroup, '-n', $WebApp, '--src', $ZipPath, '-o', 'none')
    if ($result.ExitCode -ne 0) {
        throw "Deployment failed for $WebApp (exit $($result.ExitCode)). The generic message hides the cause; read the real one with:`n  az webapp log deployment show -g $ResourceGroup -n $WebApp`n$(Get-BallastTail -Lines (@($result.ErrorText) + @($result.Output)))"
    }
    Write-BallastNote ('deploy returned after {0} s' -f $stopwatch.Elapsed.TotalSeconds.ToString('0.0', $script:Invariant))
    return $stopwatch
}

# --- Health and the switch --------------------------------------------------------------------------------------------

# The commit /health reports. Null when the key is absent or the body is not JSON, so an old copy never counts as a match.
function Get-BallastHealthCommit {
    param([AllowNull()][string]$Json)

    if ([string]::IsNullOrWhiteSpace($Json)) { return $null }
    try { $body = $Json | ConvertFrom-Json } catch { return $null }
    $commit = [string](Get-BallastProperty -Object $body -Name 'commit')
    if ([string]::IsNullOrWhiteSpace($commit)) { return $null }
    return $commit.Trim().ToLowerInvariant()
}

# Compares a reported commit with the expected full sha, by prefix. Seven characters is the least that names a commit.
function Test-BallastCommitMatches {
    param([AllowNull()][string]$Reported, [Parameter(Mandatory)][string]$Expected)

    if ([string]::IsNullOrWhiteSpace($Reported)) { return $false }
    $reportedSha = $Reported.Trim().ToLowerInvariant()
    $expectedSha = $Expected.Trim().ToLowerInvariant()
    if ($reportedSha.Length -lt 7 -or $expectedSha.Length -lt 7) { return $false }
    $length = [Math]::Min($reportedSha.Length, $expectedSha.Length)
    return ($reportedSha.Substring(0, $length) -eq $expectedSha.Substring(0, $length))
}

<#
.SYNOPSIS
    Waits until the app serves the commit and reports ready, on two polls in a row, or the timeout passes.

.DESCRIPTION
    Each poll reads <HealthUrl>/health (it must report the commit) and <HealthUrl>/health/ready (it must answer 200). The
    switch is proven on two matching polls in a row. Exit code 0 when proven, 3 when not, the same meaning as a deploy's 3.
#>
function Wait-BallastAppSwitched {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$HealthUrl,
        [Parameter(Mandatory)][string]$Commit,
        [int]$TimeoutSeconds = 900,
        [int]$PollSeconds = 5,
        [System.Diagnostics.Stopwatch]$Stopwatch = $null
    )

    $base = $HealthUrl.TrimEnd('/')
    $polling = [System.Diagnostics.Stopwatch]::StartNew()
    $streak = 0
    $poll = 0
    $noCommitWarned = $false
    while ($true) {
        $poll++
        $health = Invoke-BallastHealth -Url "$base/health?gt=$([DateTime]::UtcNow.Ticks)"
        $ready = Invoke-BallastHealth -Url "$base/health/ready"

        $reported = $null
        if ([int]$health.Status -eq 200) { $reported = Get-BallastHealthCommit -Json ([string]$health.Body) }
        if ([int]$health.Status -eq 200 -and $null -eq $reported -and -not $noCommitWarned) {
            Write-BallastWarning "$base/health reports no commit, so the switch cannot be proven."
            $noCommitWarned = $true
        }

        $isMatch = ($null -ne $reported) -and (Test-BallastCommitMatches -Reported $reported -Expected $Commit) -and ([int]$ready.Status -eq 200)
        $healthText = 'no response'
        if ([int]$health.Status -ne 0) { $healthText = [string]$health.Status }
        $readyText = 'no response'
        if ([int]$ready.Status -ne 0) { $readyText = [string]$ready.Status }
        $commitText = 'none'
        if ($null -ne $reported) { $commitText = $reported.Substring(0, [Math]::Min(8, $reported.Length)) }
        Write-BallastNote "poll $($poll): /health $healthText, commit $commitText, /health/ready $readyText"

        if ($isMatch) { $streak++ } else { $streak = 0 }
        if ($streak -ge 2) {
            $seconds = $polling.Elapsed.TotalSeconds
            if ($null -ne $Stopwatch) { $seconds = $Stopwatch.Elapsed.TotalSeconds }
            Write-BallastNote ("switched after {0} s (commit reported and ready)" -f $seconds.ToString('0.0', $script:Invariant))
            return [pscustomobject]@{
                ExitCode      = 0
                Message       = 'The new copy is live and ready.'
                Switched      = $true
                SwitchSeconds = [double]$seconds
                Proof         = 'commit reported and ready'
            }
        }
        if ($polling.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            Write-BallastWarning "$base did not show the new copy within $TimeoutSeconds s."
            return [pscustomobject]@{
                ExitCode      = 3
                Message       = "The new copy was not proven live within $TimeoutSeconds s."
                Switched      = $false
                SwitchSeconds = [double]0
                Proof         = 'not proven'
            }
        }
        if ($PollSeconds -gt 0) { Start-Sleep -Seconds $PollSeconds }
    }
}

# --- Deploy tags ------------------------------------------------------------------------------------------------------

function Get-BallastTagDateText {
    param([string]$Format = 'yyyyMMdd', [DateTime]$When = [DateTime]::UtcNow)
    return $When.ToString($Format, $script:Invariant)
}

# The next free number for deploy/<app>/<date>-<N> on this day. Tags are never reused, so the answer is the highest number
# seen locally or on origin, plus one.
function Get-BallastNextTagName {
    param([Parameter(Mandatory)][string]$App, [Parameter(Mandatory)][string]$DateText, [string]$RepoPath = '.')

    $localTags = @((Invoke-Git -RepoPath $RepoPath -Arguments @('tag', '--list', "deploy/$App/$DateText-*")).Output)
    $remoteTags = @((Invoke-Git -RepoPath $RepoPath -Arguments @('ls-remote', '--tags', 'origin')).Output)

    $pattern = '^' + [regex]::Escape("deploy/$App/$DateText-") + '(\d+)$'
    $highest = 0
    foreach ($line in @($localTags) + @($remoteTags)) {
        $text = ([string]$line).Trim()
        if (-not $text) { continue }
        # ls-remote prints "<sha><TAB>refs/tags/<name>", and an annotated tag also "<name>^{}". The name is the last field.
        $name = ($text -split '\s+')[-1] -replace '^refs/tags/', '' -replace '\^\{\}$', ''
        if ($name -cmatch $pattern) {
            $number = [int]$Matches[1]
            if ($number -gt $highest) { $highest = $number }
        }
    }
    return "deploy/$App/$DateText-$($highest + 1)"
}

<#
    Makes an annotated deploy tag and pushes it. A refused push means the number was taken after it was read, so the number is
    read again. The local tag is removed before a retry; the last attempt's tag is kept, so the owner can push it by hand.
#>
function New-BallastDeployTag {
    param(
        [Parameter(Mandatory)][string]$App,
        [Parameter(Mandatory)][string]$Sha,
        [Parameter(Mandatory)][string[]]$Message,
        [Parameter(Mandatory)][string]$DateText,
        [string]$RepoPath = '.',
        [int]$Attempts = 3,
        [int]$DelaySeconds = 5
    )

    $identity = Invoke-Git -RepoPath $RepoPath -Arguments @('var', 'GIT_COMMITTER_IDENT') -AllowFailure
    if ($identity.ExitCode -ne 0) {
        throw 'git has no committer identity in this repository, so the deploy tag cannot be made. Set user.name and user.email for it, then run again. This module never sets git config.'
    }

    $messageFile = Join-Path ([System.IO.Path]::GetTempPath()) ('ballast-tag-' + [guid]::NewGuid().ToString('N') + '.txt')
    try {
        [System.IO.File]::WriteAllText($messageFile, (($Message -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
        $name = $null
        for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
            $name = Get-BallastNextTagName -App $App -DateText $DateText -RepoPath $RepoPath
            $made = Invoke-Git -RepoPath $RepoPath -Arguments @('tag', '-a', '-F', $messageFile, $name, $Sha) -AllowFailure
            if ($made.ExitCode -ne 0) { throw "git tag $name failed: $($made.ErrorText)" }

            $pushed = Invoke-Git -RepoPath $RepoPath -Arguments @('push', 'origin', "refs/tags/$name") -AllowFailure
            if ($pushed.ExitCode -eq 0) { return [pscustomobject]@{ Pushed = $true; Name = $name } }

            Write-BallastWarning "push of $name was refused: $((([string]$pushed.ErrorText) -split "`n")[0])"
            if ($attempt -lt $Attempts) {
                $null = Invoke-Git -RepoPath $RepoPath -Arguments @('tag', '-d', $name) -AllowFailure
                if ($DelaySeconds -gt 0) { Start-Sleep -Seconds $DelaySeconds }
            }
        }
        return [pscustomobject]@{ Pushed = $false; Name = $name }
    }
    finally { Remove-Item -LiteralPath $messageFile -Force -ErrorAction SilentlyContinue }
}

# --- Migrations -------------------------------------------------------------------------------------------------------

# The .NET SDK and the dotnet-ef in use, read before anything runs. The SDK's major version must be at least the config's
# MinimumSdkMajor. After `dotnet tool restore`, the dotnet-ef in use must be the version .config/dotnet-tools.json pins.
function Test-BallastDotnetPreflight {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepoPath, [Parameter(Mandatory)][int]$MinimumSdkMajor)

    Write-BallastStep 'Preflight: .NET SDK and dotnet-ef'
    $version = Invoke-BallastProcess -FilePath 'dotnet' -Arguments @('--version') -WorkingDirectory $RepoPath
    $sdk = [string](@($version.Output | ForEach-Object { [string]$_ } | Where-Object { $_ -match '^\s*\d+\.' }) | Select-Object -First 1)
    if ($version.ExitCode -ne 0 -or $sdk -notmatch '^\s*\d+\.') {
        throw "Could not read the .NET SDK version (exit $($version.ExitCode)).`n$(Get-BallastTail -Lines $version.Output)"
    }
    $null = $sdk -match '^\s*(\d+)\.'
    if ([int]$Matches[1] -lt $MinimumSdkMajor) {
        throw "dotnet $MinimumSdkMajor or later is required (the config's MinimumSdkMajor). Found: $($sdk.Trim())."
    }
    Write-BallastNote "dotnet $($sdk.Trim())"

    $restore = Invoke-BallastProcess -FilePath 'dotnet' -Arguments @('tool', 'restore') -WorkingDirectory $RepoPath
    if ($restore.ExitCode -ne 0) { throw "dotnet tool restore failed (exit $($restore.ExitCode)).`n$(Get-BallastTail -Lines $restore.Output)" }

    $manifestPath = Join-Path $RepoPath '.config/dotnet-tools.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "There is no .config/dotnet-tools.json in $RepoPath, so the dotnet-ef version cannot be checked."
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $pinned = [string]$manifest.tools.'dotnet-ef'.version
    if (-not $pinned) { throw '.config/dotnet-tools.json does not pin dotnet-ef.' }

    $ef = Invoke-BallastProcess -FilePath 'dotnet' -Arguments @('ef', '--version') -WorkingDirectory $RepoPath
    $installed = ''
    foreach ($line in @($ef.Output | ForEach-Object { [string]$_ })) {
        if ($line -match '(\d+\.\d+\.\d+)') { $installed = $Matches[1] }
    }
    if ($ef.ExitCode -ne 0 -or -not $installed) { throw "Could not read the dotnet-ef version (exit $($ef.ExitCode)).`n$(Get-BallastTail -Lines $ef.Output)" }
    if ($installed -ne $pinned) { throw "dotnet-ef $installed runs here, but .config/dotnet-tools.json pins $pinned. Run: dotnet tool restore" }
    Write-BallastNote "dotnet-ef $installed (matches the manifest)"
}

# The connection string of an app's migrations, read from the web app. It is never printed, and it is registered as a secret.
function Get-BallastMigrationConnection {
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Migrations, [Parameter(Mandatory)][string]$ResourceGroup, [Parameter(Mandatory)][string]$WebApp)

    Write-BallastStep "Reading the connection string of $WebApp (not printed)"
    $settings = Get-BallastAppSettings -ResourceGroup $ResourceGroup -WebApp $WebApp
    if (-not $settings.ContainsKey($Migrations.ConnectionSetting)) { throw "$WebApp has no $($Migrations.ConnectionSetting) app setting." }
    $connRaw = [string]$settings[$Migrations.ConnectionSetting]
    if (-not $connRaw.Trim()) { throw "$WebApp's $($Migrations.ConnectionSetting) setting is empty." }
    if ($connRaw -like '@Microsoft.KeyVault*') {
        throw "$WebApp's connection string is a Key Vault reference. This needs the literal string, and will not guess."
    }
    Register-BallastSecret -Value $connRaw
    return $connRaw
}

# The environment of the EF commands and the bundle for one run: the app's Environment, the connection string, a fresh
# throwaway value for each ThrowawayVariables name, and a fresh folder holding each ThrowawayDirectories marker. Every folder
# made is added to Scratch, so the caller removes it whatever happens.
function New-BallastMigrationEnvironment {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Migrations,
        [Parameter(Mandatory)][string]$ConnectionString,
        [System.Collections.ArrayList]$Scratch
    )

    $childEnv = @{}
    foreach ($name in $Migrations.Environment.Keys) { $childEnv[$name] = [string]$Migrations.Environment[$name] }
    $childEnv[$Migrations.ConnectionSetting] = $ConnectionString
    foreach ($name in $Migrations.ThrowawayVariables) {
        $key = New-BallastSigningKey
        Register-BallastSecret -Value $key
        $childEnv[$name] = $key
    }
    foreach ($name in $Migrations.ThrowawayDirectories.Keys) {
        $folder = Join-Path ([System.IO.Path]::GetTempPath()) ('ballast-throwaway-' + [guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $folder
        $null = $Scratch.Add($folder)
        $null = New-Item -ItemType File -Path (Join-Path $folder ([string]$Migrations.ThrowawayDirectories[$name]))
        $childEnv[[string]$name] = $folder
    }
    return $childEnv
}

# Deletes the temporary folders and files a run made. A folder that cannot be deleted is a warning with its path, never a
# change to the run's result.
function Remove-BallastScratch {
    [CmdletBinding()]
    param([AllowNull()][System.Collections.IEnumerable]$Paths)

    foreach ($path in @($Paths)) {
        if (-not $path -or -not (Test-Path -LiteralPath $path)) { continue }
        try { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop }
        catch { Write-BallastWarning "Could not delete the temporary folder $path ($($_.Exception.Message)). Delete it by hand." }
    }
}

# The JSON array in `dotnet ef migrations list --json`, which may have warnings around it. Returns $null when there is no
# array. Entries: each has id and applied (true, false, or null when EF could not connect).
function ConvertFrom-BallastMigrationListing {
    param([AllowNull()][string[]]$Lines)

    $all = @($Lines | ForEach-Object { [string]$_ })
    $start = -1
    for ($i = 0; $i -lt $all.Count; $i++) {
        if ($all[$i].TrimStart().StartsWith('[', [StringComparison]::Ordinal)) { $start = $i }
    }
    if ($start -lt 0) { return $null }

    $end = -1
    for ($i = $start; $i -lt $all.Count; $i++) {
        if ($all[$i].TrimEnd().EndsWith(']', [StringComparison]::Ordinal)) { $end = $i }
    }
    if ($end -lt $start) { return $null }

    try { $parsed = ($all[$start..$end] -join "`n") | ConvertFrom-Json } catch { return $null }
    return [pscustomobject]@{ Entries = @(@($parsed) | Where-Object { $null -ne $_ }) }
}

# The fields of a connection string the migrations need: the server host, the database, and the password (which is
# registered as a secret). Accepts the common keyword spellings.
function Read-BallastConnectionString {
    param([Parameter(Mandatory)][string]$ConnectionString)

    $parts = @{}
    foreach ($segment in $ConnectionString.Split(';')) {
        $eq = $segment.IndexOf('=')
        if ($eq -lt 1) { continue }
        $parts[$segment.Substring(0, $eq).Trim().ToLowerInvariant()] = $segment.Substring($eq + 1).Trim()
    }
    $serverValue = ''
    foreach ($alias in @('server', 'data source', 'address', 'addr')) {
        if ($parts.ContainsKey($alias)) { $serverValue = [string]$parts[$alias]; break }
    }
    $catalog = ''
    foreach ($alias in @('initial catalog', 'database')) {
        if ($parts.ContainsKey($alias)) { $catalog = [string]$parts[$alias]; break }
    }
    $password = ''
    foreach ($alias in @('password', 'pwd')) {
        if ($parts.ContainsKey($alias)) { $password = [string]$parts[$alias]; break }
    }

    $serverName = $serverValue -replace '^tcp:', '' -replace ',\d+$', ''
    if (-not $serverName) { throw 'The connection string names no server.' }
    if (-not $catalog) { throw 'The connection string names no database (Initial Catalog).' }
    return [pscustomobject]@{ Server = $serverName; Catalog = $catalog; Password = $password }
}

function Get-BallastMigrationListing {
    param(
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$Context,
        [Parameter(Mandatory)][hashtable]$ChildEnv,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [switch]$NoBuild
    )

    $arguments = @('ef', 'migrations', 'list', '--project', $ProjectPath, '--startup-project', $ProjectPath,
        '--context', $Context, '--configuration', 'Release', '--json')
    if ($NoBuild) { $arguments += '--no-build' }
    return (Invoke-BallastProcess -FilePath 'dotnet' -Arguments $arguments -Environment $ChildEnv -WorkingDirectory $WorkingDirectory)
}

# Runs the migration-safety guard: a test run whose output may carry MIGRATION-GUARD / MIGRATION-ALLOW-LIST marker lines.
# Those marker lines are a contract with the guard, so the match is exact and case-sensitive. Verdict: clean, failed, or
# unavailable (no guard configured, its project missing, or its filter matched no tests).
function Invoke-BallastMigrationGuard {
    param([Parameter(Mandatory)][string]$RepoPath, [AllowNull()][hashtable]$Guard)

    if ($null -eq $Guard) {
        Write-BallastNote 'not available: no migration-safety guard is configured for this app'
        return [pscustomobject]@{ Verdict = 'unavailable'; Markers = @() }
    }
    $guardProject = Join-Path $RepoPath $Guard.Project
    if (-not (Test-Path -LiteralPath $guardProject -PathType Leaf)) {
        Write-BallastNote 'not available: this commit has no guard project'
        return [pscustomobject]@{ Verdict = 'unavailable'; Markers = @() }
    }

    Write-BallastNote 'running the migration-safety guard (this builds the test project)'
    $run = Invoke-BallastProcess -FilePath 'dotnet' -WorkingDirectory $RepoPath -Arguments @('test', '--project', $guardProject, '--filter-class', $Guard.Filter)
    $output = @($run.Output | ForEach-Object { [string]$_ })
    $markers = @($output | Where-Object { $_ -cmatch 'MIGRATION-(GUARD|ALLOW-LIST) ' })

    if ($run.ExitCode -ne 0) {
        if ($markers.Count -eq 0) { $markers = @(Get-BallastTail -Lines $output -Count 25) }
        return [pscustomobject]@{ Verdict = 'failed'; Markers = $markers }
    }
    if (($output -join "`n") -match 'No test matches|no tests? (matched|found|ran|were run)') {
        Write-BallastNote 'not available: the guard filter matched no tests'
        return [pscustomobject]@{ Verdict = 'unavailable'; Markers = @() }
    }
    Write-BallastNote 'guard: passed'
    return [pscustomobject]@{ Verdict = 'clean'; Markers = @() }
}

# A temporary firewall rule for this machine, so the database accepts the listing and the bundle. The name carries the time it
# was made, so a rule left by a killed run is removed once it is older than two hours. Other rules are never touched.
function New-BallastFirewallRule {
    param([Parameter(Mandatory)][string]$ResourceGroup, [Parameter(Mandatory)][string]$Server, [Parameter(Mandatory)][string]$Ip)

    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", $script:Invariant)
    $name = 'ballast-deploy-' + $stamp + '-' + ([guid]::NewGuid().ToString('N')).Substring(0, 4)
    $null = Invoke-Az -Arguments @('sql', 'server', 'firewall-rule', 'create', '-g', $ResourceGroup, '-s', $Server, '-n', $name,
        '--start-ip-address', $Ip, '--end-ip-address', $Ip, '-o', 'none')
    return $name
}

function Set-BallastFirewallAddress {
    param([Parameter(Mandatory)][string]$ResourceGroup, [Parameter(Mandatory)][string]$Server, [Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Ip)
    $null = Invoke-Az -Arguments @('sql', 'server', 'firewall-rule', 'update', '-g', $ResourceGroup, '-s', $Server, '-n', $Name,
        '--start-ip-address', $Ip, '--end-ip-address', $Ip, '-o', 'none')
}

function Remove-BallastStaleFirewallRules {
    param([Parameter(Mandatory)][string]$ResourceGroup, [Parameter(Mandatory)][string]$Server)

    $listed = @((Invoke-Az -Arguments @('sql', 'server', 'firewall-rule', 'list', '-g', $ResourceGroup, '-s', $Server,
            '--query', "[?starts_with(name, 'ballast-deploy-')].name", '-o', 'tsv')).Output)
    $cutoff = [DateTime]::UtcNow.AddHours(-2)
    foreach ($raw in $listed) {
        $name = ([string]$raw).Trim()
        if ($name -cnotmatch '^ballast-deploy-([0-9]{8}T[0-9]{6}Z)-[0-9a-f]{4}$') { continue }
        $stamp = [DateTime]::ParseExact($Matches[1], "yyyyMMdd'T'HHmmss'Z'", $script:Invariant,
            [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal)
        if ($stamp -gt $cutoff) { continue }
        Write-BallastNote "removing a rule left by an earlier run: $name"
        $null = Invoke-Az -Arguments @('sql', 'server', 'firewall-rule', 'delete', '-g', $ResourceGroup, '-s', $Server, '-n', $name, '-o', 'none')
    }
}

function Remove-BallastFirewallRule {
    param([Parameter(Mandatory)][string]$ResourceGroup, [Parameter(Mandatory)][string]$Server, [Parameter(Mandatory)][string]$Name)

    $verified = $false
    try {
        $null = Invoke-Az -Arguments @('sql', 'server', 'firewall-rule', 'delete', '-g', $ResourceGroup, '-s', $Server, '-n', $Name, '-o', 'none')
        $left = (@((Invoke-Az -Arguments @('sql', 'server', 'firewall-rule', 'list', '-g', $ResourceGroup, '-s', $Server,
                    '--query', "[?name=='$Name'].name", '-o', 'tsv')).Output) -join '').Trim()
        $verified = [string]::IsNullOrEmpty($left)
    }
    catch {
        Write-BallastWarning $_.Exception.Message
    }
    if ($verified) {
        Write-BallastNote "firewall rule $Name removed"
    }
    else {
        Write-BallastWarning "firewall rule $Name may still be open. Remove it by hand: az sql server firewall-rule delete -g $ResourceGroup -s $Server -n $Name"
    }
}

# One migration run for one app: preflight, the connection string, the optional firewall rule, the pending list, the guard,
# the summary, the prompt, the bundle, and the post-check. Returns Result, Code and Message. Sets State.Pending.
function Invoke-BallastMigrationPlan {
    param(
        [Parameter(Mandatory)][hashtable]$Config,
        [Parameter(Mandatory)][string]$App,
        [Parameter(Mandatory)][hashtable]$AppConfig,
        [Parameter(Mandatory)][string]$RepoPath,
        [string]$BundlePath,
        [switch]$WhatIf,
        [switch]$Yes,
        [Parameter(Mandatory)][hashtable]$State,
        [string]$Database
    )

    $mig = $AppConfig.Migrations
    if ($null -eq $mig) { throw "$App has no Migrations section in the config, so there is nothing to migrate." }
    $webApp = $AppConfig.WebApp
    $group = $AppConfig.ResourceGroup


    $projectPath = Join-Path $RepoPath $AppConfig.Project
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw "The project $($AppConfig.Project) is not in $RepoPath." }

    # dotnet ef reads the project's restored package assets before it builds anything, so in a folder that was never
    # restored (the clean worktree, a fresh clone) it fails with "Unable to retrieve project metadata". The restore runs
    # here, before any Azure call, and a failed restore stops the run.
    Write-BallastStep "Restoring $($AppConfig.Project)"
    $restore = Invoke-BallastProcess -FilePath 'dotnet' -Arguments @('restore', $projectPath) -WorkingDirectory $RepoPath
    if ($restore.ExitCode -ne 0) { throw "dotnet restore $($AppConfig.Project) failed (exit $($restore.ExitCode)).`n$(Get-BallastTail -Lines $restore.Output)" }

    $connRaw = Get-BallastMigrationConnection -Migrations $mig -ResourceGroup $group -WebApp $webApp
    $info = Read-BallastConnectionString -ConnectionString $connRaw
    Register-BallastSecret -Value $info.Password

    if ($Database) {
        if ($Database -cnotmatch '^[A-Za-z0-9_.-]{1,128}$') { throw "The rehearsal database name '$Database' is not a database name. Refusing." }
        if ($Database -ieq $info.Catalog) {
            throw "-Database names $($info.Catalog), the app's own database. A rehearsal needs another database on the same server, so nothing is migrated here. Refusing."
        }
        $connRaw = [regex]::Replace($connRaw, '(?i)((?:initial catalog|database)=)[^;]*', ('${1}' + $Database))
        $info = Read-BallastConnectionString -ConnectionString $connRaw
        if ($info.Catalog -cne $Database) { throw "The rehearsal database name could not be set in the connection string. Refusing." }
        Write-BallastNote "rehearsal: $Database on the same server, instead of the app's own database"
    }

    $childEnv = New-BallastMigrationEnvironment -Migrations $mig -ConnectionString $connRaw -Scratch $State.Scratch

    if ($mig.SqlServer) {
        if (($info.Server -split '\.')[0] -ine $mig.SqlServer) {
            throw "The connection string names '$($info.Server)', but the config's SqlServer is '$($mig.SqlServer)'. Refusing."
        }
        Write-BallastStep "Database $($mig.SqlServer)/$($info.Catalog)"
        $exists = Invoke-Az -AllowFailure -Arguments @('sql', 'db', 'show', '-g', $group, '-s', $mig.SqlServer, '-n', $info.Catalog, '-o', 'none')
        if ($exists.ExitCode -ne 0) {
            throw "Database '$($info.Catalog)' was not found on '$($mig.SqlServer)' in '$group'. Refusing: EF would create an empty database from a typo."
        }
        if ($AppConfig.RequireCaseInsensitiveCollation) {
            $query = Invoke-Az -Arguments @('sql', 'db', 'show', '-g', $group, '-s', $mig.SqlServer, '-n', $info.Catalog, '--query', 'collation', '-o', 'tsv')
            $collation = (@($query.Output | ForEach-Object { [string]$_ }) -join '').Trim()
            if (-not $collation) {
                throw "Could not read the collation of '$($info.Catalog)'. Refusing: the database is not checked, so nothing is migrated."
            }
            if ($collation -cnotmatch '_CI_') {
                throw "$($info.Catalog) has collation '$collation', which is case-sensitive. This app requires a case-insensitive database, so nothing is migrated here."
            }
        }
        Write-BallastStep 'Temporary firewall rule for this machine'
        Remove-BallastStaleFirewallRules -ResourceGroup $group -Server $mig.SqlServer
        $State.ResourceGroup = $group
        $State.Server = $mig.SqlServer
        $State.Ip = Get-BallastPublicIPv4
        $State.Rule = New-BallastFirewallRule -ResourceGroup $group -Server $mig.SqlServer -Ip $State.Ip
        Write-BallastNote "$($State.Rule) -> $($State.Ip) (removed when this run ends)"
    }

    Write-BallastStep 'Pending migrations'
    $built = $false
    $attempt = 0
    $ipUpdated = $false
    $entries = @()
    $retries = [int]$Config.Options.ListRetries
    $delay = [int]$Config.Options.ListRetryDelaySeconds
    while ($true) {
        $attempt++
        $listing = Get-BallastMigrationListing -ProjectPath $projectPath -Context $mig.DbContext -ChildEnv $childEnv -WorkingDirectory $RepoPath -NoBuild:$built
        $built = $true
        $text = (@($listing.Output | ForEach-Object { [string]$_ })) -join "`n"
        $parsed = ConvertFrom-BallastMigrationListing -Lines @($listing.Output)
        $unknown = $false
        if ($null -eq $parsed) {
            # No list at all. Only a refused connection is worth another try; anything else is a build or setup fault.
            if ($text -notmatch '40615|not allowed to access|Cannot open server') {
                throw "dotnet ef migrations list gave no migration list (exit $($listing.ExitCode)).`n$(Get-BallastTail -Lines $listing.Output)"
            }
            $unknown = $true
        }
        else {
            $entries = @($parsed.Entries)
            if ($entries.Count -eq 0) { throw "dotnet ef found no migrations for context $($mig.DbContext) in $($AppConfig.Project). Wrong project or context." }
            $unknown = (@($entries | Where-Object { $null -eq (Get-BallastProperty -Object $_ -Name 'applied') }).Count -gt 0)
        }
        if (-not $unknown) { break }

        # SQL names the address it refused. If it is not the one the rule allows, the rule is updated once.
        if ($mig.SqlServer -and -not $ipUpdated) {
            $seen = [regex]::Match($text, "IP address '([0-9]{1,3}(?:\.[0-9]{1,3}){3})'")
            if ($seen.Success -and $seen.Groups[1].Value -ne $State.Ip) {
                Write-BallastWarning "SQL sees this machine as $($seen.Groups[1].Value), not $($State.Ip). Updating the temporary rule once."
                Set-BallastFirewallAddress -ResourceGroup $group -Server $mig.SqlServer -Name $State.Rule -Ip $seen.Groups[1].Value
                $State.Ip = $seen.Groups[1].Value
                $ipUpdated = $true
                continue
            }
        }
        if ($attempt -gt $retries) {
            throw "Could not read the migration list from $($info.Catalog) after $attempt attempts. Check the database and its firewall, and whether this machine's public address has changed since the rule was written."
        }
        Write-BallastNote "the database did not answer (attempt $attempt of $($retries + 1)); waiting $delay s"
        if ($delay -gt 0) { Start-Sleep -Seconds $delay }
    }

    $pending = @($entries | Where-Object { (Get-BallastProperty -Object $_ -Name 'applied') -eq $false })
    $total = $entries.Count
    $State.Pending = $pending.Count
    Write-BallastStep "$total migration(s) in this code, $($pending.Count) pending for $($info.Catalog)"

    if ($pending.Count -eq 0) {
        Write-BallastStep "Nothing pending: all $total of this code's migrations are applied."
        return @{ Result = 'nothing-pending'; Code = 0; Message = 'Nothing pending.' }
    }
    if ($pending.Count -eq $total) {
        Write-BallastWarning "All $total migrations are pending. The database looks empty, or it is the wrong one ($($info.Catalog))."
        if ($Yes) { throw 'All migrations are pending and -Yes was given. Refusing.' }
    }

    Write-BallastStep 'Migration-safety guard'
    $guard = Invoke-BallastMigrationGuard -RepoPath $RepoPath -Guard $mig.Guard
    if ($guard.Verdict -eq 'failed') {
        foreach ($line in @($guard.Markers)) { Write-Host ('    ' + (Hide-BallastSecret -Text ([string]$line))) -ForegroundColor Red }
        $names = (@($pending | ForEach-Object { Get-BallastProperty -Object $_ -Name 'id' })) -join ', '
        throw "The migration-safety guard failed. Pending: $names. Make the migration additive (expand now, contract later), or add a reviewed allow-list entry in the guard's tests."
    }

    Write-BallastStep "Summary for $($info.Catalog) ($App)"
    $verdictText = 'guard not available for this commit'
    if ($guard.Verdict -eq 'clean') { $verdictText = 'passes the guard' }
    foreach ($migration in $pending) { Write-BallastNote "  $(Get-BallastProperty -Object $migration -Name 'id')  $verdictText" }
    Write-BallastNote 'The firewall rule was opened to read this list. It is removed when the run ends.'

    if ($WhatIf) {
        Write-BallastStep 'WhatIf: nothing applied.'
        return @{ Result = 'whatif'; Code = 0; Message = 'WhatIf: nothing applied.' }
    }

    if ($guard.Verdict -eq 'unavailable') {
        if ($Yes) { throw 'The guard is not available for this commit, and -Yes does not accept that. Refusing.' }
        if (-not (Confirm-BallastOwner -Question 'The guard is not available for this commit. Apply the pending migrations without it?')) {
            throw 'Stopped: the guard is not available for this commit, and the owner did not accept applying without it.'
        }
    }

    if ($BundlePath -and -not (Test-Path -LiteralPath $BundlePath -PathType Leaf)) {
        throw "No migration bundle at '$BundlePath'. Pass a bundle built with dotnet ef migrations bundle, or leave -BundlePath out to build it from the commit."
    }

    if (-not $Yes) {
        if (-not (Confirm-BallastOwner -Question "Apply $($pending.Count) migration(s) to $($info.Catalog)?")) {
            Write-BallastStep 'Declined. Nothing was applied.'
            return @{ Result = 'declined'; Code = 2; Message = 'Declined. Nothing was applied.' }
        }
    }

    if (-not $BundlePath) {
        Write-BallastStep "Building the migration bundle for $($mig.DbContext) from the commit"
        $bundleDir = Join-Path ([System.IO.Path]::GetTempPath()) ('ballast-bundle-' + [guid]::NewGuid().ToString('N'))
        $null = $State.Scratch.Add($bundleDir)
        $BundlePath = New-BallastMigrationBundle -WorktreeDir $RepoPath -Project $AppConfig.Project -Context $mig.DbContext -OutDir $bundleDir -Environment $childEnv
    }

    Write-BallastStep "Applying $($pending.Count) migration(s) to $($info.Catalog)"
    $utc = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $script:Invariant)
    Write-BallastNote "UTC before applying: $utc (the reference point for a point-in-time restore)"
    $bundle = Invoke-BallastProcess -FilePath $BundlePath -Arguments @() -Environment $childEnv -WorkingDirectory (Split-Path -Parent $BundlePath) -Stream
    if ($bundle.ExitCode -ne 0) {
        Write-BallastNote 'Migrations before the failing one stay applied: each runs in its own transaction, and they are additive, so the code now serving keeps working.'
        Write-BallastNote 'Nothing is rolled back. Run this again to resume.'
        throw "The migration bundle failed (exit $($bundle.ExitCode))."
    }

    Write-BallastStep 'Post-check: nothing pending'
    $after = Get-BallastMigrationListing -ProjectPath $projectPath -Context $mig.DbContext -ChildEnv $childEnv -WorkingDirectory $RepoPath -NoBuild
    $afterParsed = ConvertFrom-BallastMigrationListing -Lines @($after.Output)
    $stillOpen = -1
    if ($null -ne $afterParsed) {
        $stillOpen = @($afterParsed.Entries | Where-Object { (Get-BallastProperty -Object $_ -Name 'applied') -ne $true }).Count
    }
    if ($stillOpen -ne 0) {
        throw "After the bundle ran, $stillOpen migration(s) are still pending or unknown. Check the database before continuing."
    }
    Write-BallastNote "applied $($pending.Count) migration(s)"
    return @{ Result = 'applied'; Code = 0; Message = "Applied $($pending.Count) migration(s)." }
}

<#
.SYNOPSIS
    Applies one app's pending EF Core migrations from a bundle, once the migration-safety guard has passed.

.DESCRIPTION
    Exit codes (frozen):
      0  applied, nothing pending, or a finished -WhatIf
      1  any failure: a guard refusal, an unreadable list, a missing bundle, or a bundle that failed
      2  declined at the y/N prompt
    The last line printed is APPLY-MIGRATIONS result=<applied|nothing-pending|whatif|declined|failed> app=<app> pending=<n>.
    The prompt defaults to no. -Yes skips the prompt only; the guard checks still run.
#>
function Invoke-BallastMigrations {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Config,
        [Parameter(Mandatory)][string]$App,
        [string]$RepoPath = '.',
        [string]$BundlePath,
        [string]$Ref = 'HEAD',
        [string]$Database,
        [switch]$WhatIf,
        [switch]$Yes
    )

    $state = @{ Rule = $null; Server = $null; ResourceGroup = $null; Ip = $null; Pending = 0; PreviousSubscription = $null; Worktree = $null; Scratch = [System.Collections.ArrayList]::new() }
    $outcome = @{ Result = 'failed'; Code = 1; Message = '' }
    $repo = $RepoPath
    try {
        $cfg = Resolve-BallastConfig -Config $Config
        $script:SecretPatterns = @($cfg.Options.SecretPatterns)
        $repo = (Resolve-Path -LiteralPath $RepoPath).Path
        $appConfig = Get-BallastAppConfig -Config $cfg -App $App
        if ($null -eq $appConfig.Migrations) { throw "$App has no Migrations section in the config, so there is nothing to migrate." }

        # Without -BundlePath the migrations are those of one commit, and the code they run from is a clean worktree of it.
        $planRepo = $repo
        if (-not $BundlePath) {
            $resolved = Invoke-Git -RepoPath $repo -Arguments @('rev-parse', '--verify', "$Ref^{commit}") -AllowFailure
            $sha = ''
            if ($resolved.ExitCode -eq 0) { $sha = ([string](@($resolved.Output | ForEach-Object { [string]$_ }) | Select-Object -First 1)).Trim() }
            if ($sha -cnotmatch '^[0-9a-f]{40}$') { throw "-Ref '$Ref' is not a commit in this clone, so its migrations cannot be read." }
            $state.Worktree = Join-Path ([System.IO.Path]::GetTempPath()) ('ballast-migrations-' + [guid]::NewGuid().ToString('N'))
            $planRepo = New-BallastBuildWorktree -Sha $sha -Root $state.Worktree -RepoPath $repo
            Write-BallastNote "migrations of $($sha.Substring(0, 8)), run from a clean worktree of that commit"
        }

        Test-BallastDotnetPreflight -RepoPath $planRepo -MinimumSdkMajor ([int]$cfg.Options.MinimumSdkMajor)
        $state.PreviousSubscription = Use-BallastSubscription -Subscription $appConfig.Subscription
        $outcome = Invoke-BallastMigrationPlan -Config $cfg -App $App -AppConfig $appConfig -RepoPath $planRepo -BundlePath $BundlePath -WhatIf:$WhatIf -Yes:$Yes -State $state -Database $Database
    }
    catch {
        $text = Hide-BallastSecret -Text $_.Exception.Message
        Write-BallastWarning "FAILED: $text"
        $outcome = @{ Result = 'failed'; Code = 1; Message = $text }
    }
    finally {
        if ($state.Rule) { Remove-BallastFirewallRule -ResourceGroup $state.ResourceGroup -Server $state.Server -Name $state.Rule }
        if ($state.Worktree) { Remove-BallastBuildWorktree -Root $state.Worktree -RepoPath $repo }
        Remove-BallastScratch -Paths $state.Scratch
        Restore-BallastSubscription -Id $state.PreviousSubscription
    }

    Write-Host "APPLY-MIGRATIONS result=$($outcome.Result) app=$App pending=$($state.Pending)"
    return [pscustomobject]@{
        ExitCode = [int]$outcome.Code
        Message  = [string]$outcome.Message
        Result   = [string]$outcome.Result
        Pending  = [int]$state.Pending
        App      = $App
    }
}

# --- Deploy -----------------------------------------------------------------------------------------------------------

<#
.SYNOPSIS
    Deploys one or more apps from a pushed commit: guards, a clean build, migrations, a zip deploy, proof of the switch, a tag.

.DESCRIPTION
    Phase 0 changes nothing in Azure: it fetches origin, resolves -Ref, refuses a commit not on origin, selects each app's
    subscription, checks each app runs from its package and has no forbidden setting, and asks y/N.
    Phase 1 builds the commit in a temporary worktree, stamped with its build time, and zips each app.
    Phase 2, app by app in the order given: migrations (when the app has them), the zip deploy, the proof of the switch, and
    the tag. A failure stops the run before the next app starts. The temporary worktree is removed and the az default
    subscription is put back in every case.

    Exit codes (frozen, as the routine deploy has always used them):
      0  deployed, or -WhatIf finished, or the y/N prompt was answered no (nothing deployed)
      1  failure, a guard refused the deploy, or a value could not be read
      2  the owner declined the migrations, or -RefusePendingMigrations found some pending; nothing deployed for that app
      3  deployed but not proven live within -TimeoutSeconds, so no tag
      4  deployed and healthy, but the tag push failed (the commands to push it are printed)
#>
function Invoke-BallastDeploy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Config,
        [Parameter(Mandatory)][string[]]$App,
        [string]$Ref = 'HEAD',
        [string]$RepoPath = '.',
        [switch]$Yes,
        [switch]$WhatIf,
        [switch]$RefusePendingMigrations,
        [int]$TimeoutSeconds = 900,
        [int]$PollSeconds = 5
    )

    $state = @{ TempRoot = $null; PreviousSubscription = $null; Code = 1; Message = ''; Apps = @() }
    $repo = $RepoPath
    try {
        $cfg = Resolve-BallastConfig -Config $Config
        $script:SecretPatterns = @($cfg.Options.SecretPatterns)
        $repo = (Resolve-Path -LiteralPath $RepoPath).Path
        if (@($App | Select-Object -Unique).Count -ne $App.Count) { throw 'An app is named more than once. Name each app once, in the order to deploy them.' }
        foreach ($key in $App) { $null = Get-BallastAppConfig -Config $cfg -App $key }
        $tagDate = Get-BallastTagDateText -Format ([string]$cfg.Options.TagDateFormat)
        $tagAttempts = [int]$cfg.Options.TagPushAttempts
        $tagDelay = [int]$cfg.Options.TagPushDelaySeconds

        # --- Phase 0: guards. Nothing changes in Azure here. ---
        Write-BallastStep 'Fetching origin'
        $null = Invoke-Git -RepoPath $repo -Arguments @('fetch', 'origin', '--tags')

        $target = Resolve-BallastRef -Ref $Ref -App $App -RepoPath $repo
        $shortSha = $target.Sha.Substring(0, 8)
        Assert-BallastDeployableCommit -Sha $target.Sha -RepoPath $repo

        # Checked here, before anything is deployed, as well as at the tag: a missing identity found only at the tag would
        # leave a live app with no tag.
        $identity = Invoke-Git -RepoPath $repo -Arguments @('var', 'GIT_COMMITTER_IDENT') -AllowFailure
        if ($identity.ExitCode -ne 0) {
            Stop-BallastRun 1 'git has no committer identity in this repository. Set user.name and user.email for it, then deploy again. This module never sets git config.'
        }

        if (@($App | Where-Object { $null -ne $cfg.Apps[$_].Migrations }).Count -gt 0) {
            Test-BallastDotnetPreflight -RepoPath $repo -MinimumSdkMajor ([int]$cfg.Options.MinimumSdkMajor)
        }

        $liveByApp = @{}
        foreach ($key in $App) {
            $appConfig = $cfg.Apps[$key]
            $previous = Use-BallastSubscription -Subscription $appConfig.Subscription
            if ($null -eq $state.PreviousSubscription) { $state.PreviousSubscription = $previous }

            Write-BallastStep "Checking $($appConfig.WebApp)"
            $settings = Get-BallastAppSettings -ResourceGroup $appConfig.ResourceGroup -WebApp $appConfig.WebApp
            $runFromPackage = Test-BallastRunFromPackage -Settings $settings -WebApp $appConfig.WebApp
            Write-BallastLines -Result $runFromPackage
            if (-not $runFromPackage.Ok) {
                Stop-BallastRun 1 "$($appConfig.WebApp) is not set up to run from its package, so nothing was deployed. Fix the settings listed above, then run again."
            }
            $forbidden = Test-BallastForbiddenSettings -Settings $settings -Forbidden @($cfg.Options.ForbiddenSettings) -ResourceGroup $appConfig.ResourceGroup -WebApp $appConfig.WebApp
            Write-BallastLines -Result $forbidden
            if (-not $forbidden.Ok) {
                Stop-BallastRun 1 "$($appConfig.WebApp) has a setting the deploy refuses, so nothing was deployed. Remove it as the line above says, then run again."
            }

            $health = Invoke-BallastHealth -Url "$($appConfig.HealthUrl)/health?gt=$([DateTime]::UtcNow.Ticks)"
            $live = Get-BallastHealthCommit -Json ([string]$health.Body)
            $liveByApp[$key] = $live
            $liveText = 'unreachable'
            if ($null -ne $live) { $liveText = $live.Substring(0, [Math]::Min(8, $live.Length)) }
            elseif ([int]$health.Status -eq 200) { $liveText = 'not reported (/health has no commit yet)' }
            elseif ([int]$health.Status -ne 0) { $liveText = "HTTP $([int]$health.Status)" }
            Write-BallastNote "$($appConfig.WebApp) live now: $liveText"

            if ($null -ne $live) {
                if (Test-BallastCommitMatches -Reported $live -Expected $target.Sha) {
                    Write-BallastNote "$($appConfig.WebApp) already runs $shortSha; this redeploys it."
                }
                else {
                    $ancestor = Invoke-Git -RepoPath $repo -Arguments @('merge-base', '--is-ancestor', $target.Sha, $live) -AllowFailure
                    if ($ancestor.ExitCode -eq 0) {
                        Write-BallastWarning "ROLLBACK: $($appConfig.WebApp) would go back to $shortSha, which is older than the live commit."
                    }
                    elseif ($ancestor.ExitCode -ne 1) {
                        Write-BallastNote "Cannot tell whether $shortSha is older than the live commit: it is not in this clone."
                    }
                }
            }
        }

        Write-BallastStep 'Deploy'
        Write-BallastNote "Apps:   $($App -join ', ')"
        Write-BallastNote "Commit: $($target.Sha)"
        Write-BallastNote "        $($target.Subject)"
        if ($target.TagName) { Write-BallastNote "Tag:    $($target.TagName)" }
        if ($WhatIf) {
            Write-BallastWarning '-WhatIf: the guards and the build run. Nothing is migrated, deployed or tagged.'
        }
        elseif (-not (Confirm-BallastOwner -Question "Deploy $($App -join ' and ') at ${shortSha}?" -Yes:$Yes)) {
            Stop-BallastRun 0 'Not deployed: answered no. Nothing was changed.'
        }

        # --- Phase 1: build. The database is not touched yet. ---
        $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ballast-deploy-" + $shortSha + "-" + $PID)
        $state.TempRoot = $tempRoot
        Write-BallastStep "Building $shortSha in a clean worktree"
        $worktree = New-BallastBuildWorktree -Sha $target.Sha -Root $tempRoot -RepoPath $repo

        $buildTime = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $script:Invariant)
        $zips = @{}
        $bundles = @{}
        foreach ($key in $App) {
            $appConfig = $cfg.Apps[$key]
            $outDir = Join-Path $tempRoot ("out/" + $key)
            Invoke-BallastPublish -WorktreeDir $worktree -Project $appConfig.Project -OutDir $outDir -Sha $target.Sha -BuildTimeUtc $buildTime `
                -RuntimeIdentifier ([string]$cfg.Options.RuntimeIdentifier) -LegacyBuildTimeProperty $appConfig.LegacyBuildTimeProperty
            $null = New-BallastDeploymentZip -SourceDir $outDir -ZipPath "$outDir.zip"
            $zips[$key] = "$outDir.zip"
            if ($null -ne $appConfig.Migrations -and -not $WhatIf) {
                $null = Use-BallastSubscription -Subscription $appConfig.Subscription
                $connRaw = Get-BallastMigrationConnection -Migrations $appConfig.Migrations -ResourceGroup $appConfig.ResourceGroup -WebApp $appConfig.WebApp
                $scratch = [System.Collections.ArrayList]::new()
                try {
                    $bundleEnv = New-BallastMigrationEnvironment -Migrations $appConfig.Migrations -ConnectionString $connRaw -Scratch $scratch
                    $bundles[$key] = New-BallastMigrationBundle -WorktreeDir $worktree -Project $appConfig.Project `
                        -Context $appConfig.Migrations.DbContext -OutDir (Join-Path $tempRoot ("bundle/" + $key)) -Environment $bundleEnv
                }
                finally { Remove-BallastScratch -Paths $scratch }
            }
        }

        # --- Phase 2: migrate, deploy, prove, tag. ---
        if ($WhatIf) {
            foreach ($key in $App) {
                $appConfig = $cfg.Apps[$key]
                $webApp = $appConfig.WebApp
                $preview = Get-BallastNextTagName -App $key -DateText $tagDate -RepoPath $repo
                if ($null -ne $appConfig.Migrations) {
                    Write-BallastNote "would migrate ${webApp}: the pending migrations, after the guard, with the y/N prompt"
                    if ($RefusePendingMigrations) {
                        $pre = Invoke-BallastMigrations -Config $cfg -App $key -RepoPath $worktree -WhatIf
                        if ($pre.ExitCode -ne 0) { Write-BallastWarning "would stop: could not read the pending migrations for $webApp" }
                        elseif ($pre.Pending -gt 0) { Write-BallastWarning "would stop: $($pre.Pending) migration(s) pending for $webApp. Nothing would be deployed for it." }
                        else { Write-BallastNote "would continue: no migration pending for $webApp" }
                    }
                }
                Write-BallastNote "would deploy $($zips[$key]) to $webApp by config-zip"
                Write-BallastNote "would wait up to $TimeoutSeconds s for $webApp to serve $shortSha and be ready"
                Write-BallastNote "would tag $preview at $shortSha once it is proven live"
            }
            Write-BallastStep 'nothing deployed (-WhatIf)'
        }
        else {
            foreach ($key in $App) {
                $appConfig = $cfg.Apps[$key]
                $webApp = $appConfig.WebApp
                $null = Use-BallastSubscription -Subscription $appConfig.Subscription

                if ($null -ne $appConfig.Migrations) {
                    if ($RefusePendingMigrations) {
                        Write-BallastStep "Pending migrations: $webApp"
                        $pre = Invoke-BallastMigrations -Config $cfg -App $key -RepoPath $worktree -WhatIf
                        if ($pre.ExitCode -ne 0) { Stop-BallastRun 1 "could not read the pending migrations for $webApp; nothing deployed for it" }
                        if ($pre.Pending -gt 0) {
                            Stop-BallastRun 2 "$($pre.Pending) migration(s) pending for $webApp. Nothing was deployed for it. Run the deploy again with migrations allowed."
                        }
                        Write-BallastNote "none pending for $webApp"
                    }
                    Write-BallastStep "Migrations: $webApp"
                    $migrated = Invoke-BallastMigrations -Config $cfg -App $key -RepoPath $worktree -BundlePath $bundles[$key] -Yes:$Yes
                    if ($migrated.ExitCode -eq 2) { Stop-BallastRun 2 "The migrations were declined for $webApp, so it was not deployed." }
                    if ($migrated.ExitCode -ne 0) { Stop-BallastRun 1 "Migrations failed for $webApp (exit $($migrated.ExitCode)). Stopped before deploying it." }
                }

                Write-BallastStep "Deploying $webApp"
                $stopwatch = Invoke-BallastZipDeploy -ResourceGroup $appConfig.ResourceGroup -WebApp $webApp -ZipPath $zips[$key]
                $proof = Wait-BallastAppSwitched -HealthUrl $appConfig.HealthUrl -Commit $target.Sha -TimeoutSeconds $TimeoutSeconds -PollSeconds $PollSeconds -Stopwatch $stopwatch
                if (-not $proof.Switched) {
                    Stop-BallastRun 3 "$webApp was deployed but not proven live within $TimeoutSeconds s, so there is no tag. Check: az webapp log tail -g $($appConfig.ResourceGroup) -n $webApp"
                }

                $previous = 'not reported'
                if ($null -ne $liveByApp[$key]) { $previous = $liveByApp[$key] }
                $deployedUtc = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", $script:Invariant)
                # Commit, subject, app, timing and the ref as given. No setting values: this text is kept in git.
                $tagMessage = @(
                    "commit: $($target.Sha)",
                    "subject: $($target.Subject)",
                    "app: $key",
                    "deployed-utc: $deployedUtc",
                    "switch-seconds: $($proof.SwitchSeconds.ToString('0.0', $script:Invariant))",
                    "proof: $($proof.Proof)",
                    "previous-commit: $previous",
                    "ref: $Ref"
                )
                $tag = $null
                try {
                    $tag = New-BallastDeployTag -App $key -Sha $target.Sha -Message $tagMessage -DateText $tagDate `
                        -RepoPath $repo -Attempts $tagAttempts -DelaySeconds $tagDelay
                }
                catch {
                    # Still deployed and healthy, so a failure to make the tag is exit 4, not a failed deploy.
                    Write-BallastWarning "$webApp is deployed and healthy at $shortSha, but the tag could not be made: $(Hide-BallastSecret -Text $_.Exception.Message)"
                    Stop-BallastRun 4 "No tag for $webApp. Make one for $shortSha by hand as deploy/$key/$tagDate-<N>, with N one past the highest existing."
                }
                if (-not $tag.Pushed) {
                    Write-BallastWarning "$webApp is deployed and healthy at $shortSha, but its tag was not pushed. Push it by hand:"
                    Write-BallastNote "  git push origin refs/tags/$($tag.Name)"
                    Stop-BallastRun 4 "The tag push failed for $webApp."
                }
                Write-BallastStep "Tagged $($tag.Name) at $shortSha"
                $state.Apps += [pscustomobject]@{ App = $key; WebApp = $webApp; Tag = $tag.Name }
            }
            Write-BallastStep 'Done'
        }

        $state.Code = 0
        $state.Message = 'Deployed.'
        if ($WhatIf) { $state.Message = 'WhatIf: nothing deployed.' }
    }
    catch {
        $code = 1
        if ($_.Exception.Data.Contains('BallastExit')) { $code = [int]$_.Exception.Data['BallastExit'] }
        $text = Hide-BallastSecret -Text $_.Exception.Message
        if ($code -eq 0) { Write-BallastNote $text } else { Write-BallastWarning $text }
        $state.Code = $code
        $state.Message = $text
    }
    finally {
        if ($state.TempRoot) { Remove-BallastBuildWorktree -Root $state.TempRoot -RepoPath $repo }
        Restore-BallastSubscription -Id $state.PreviousSubscription
    }

    return [pscustomobject]@{
        ExitCode = [int]$state.Code
        Message  = [string]$state.Message
        Apps     = @($state.Apps)
    }
}

Export-ModuleMember -Function Read-BallastConfig, Invoke-BallastDeploy, Invoke-BallastMigrations, Wait-BallastAppSwitched, Hide-BallastSecret

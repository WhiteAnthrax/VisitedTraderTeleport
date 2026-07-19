<#
.SYNOPSIS
Builds and verifies one remotely resolved VisitedTraderTeleport commit on Windows.

.DESCRIPTION
Isolated mode (the default) creates a unique disposable repository under WorkRoot,
fetches only the remotely advertised target, checks it out detached, and removes
the workspace unless KeepWorkspace is set. Prepared mode is an explicit opt-in
that builds an existing clean checkout without fetching, checking out, resetting,
cleaning, or stashing it; remote verification uses a separate disposable probe.

Ref accepts branch:<name>, tag:<name>, commit:<40-hex-sha>, an unprefixed full
SHA, or an unprefixed name. An unprefixed name that exists as both a remote branch
and tag is rejected as ambiguous. DryRun performs remote and game-reference
preflight checks without copying references or running build/package commands.

.PARAMETER Mode
Isolated (default) or Prepared. Prepared requires RepositoryPath.

.PARAMETER Ref
Remote-authoritative branch, tag, or full commit identifier.

.PARAMETER RemoteUrl
Git remote queried with ls-remote and used for bounded fetches. Defaults to
VTT_BUILD_REMOTE_URL or the public repository URL.

.PARAMETER RepositoryPath
Existing checkout used only in Prepared mode.

.PARAMETER GamePath
7 Days to Die installation root. Defaults to VTT_GAME_PATH.

.PARAMETER OutputRoot
Persistent logs and artifacts root. Defaults to VTT_BUILD_ROOT or LOCALAPPDATA.

.PARAMETER WorkRoot
Parent for script-owned disposable workspaces. Defaults to VTT_BUILD_WORK_ROOT
or <OutputRoot>\workspaces.

.PARAMETER DotNetPath
dotnet executable name or path. Defaults to VTT_DOTNET_PATH or dotnet.

.PARAMETER GitPath
Git executable name or path. Defaults to VTT_GIT_PATH or git.

.PARAMETER TimeoutSeconds
Overall wall-clock deadline in seconds. Default: 1800.

.PARAMETER DryRun
Performs the remote, executable, game-reference, and mode-specific preflight
without copying references, building, running package checks, or collecting an
artifact.

.PARAMETER KeepWorkspace
Retains the script-owned Isolated workspace after completion. Invalid in
Prepared mode.

.PARAMETER SkipPackageChecks
Skips ModChecks after the Release build. Intended only as an explicit diagnostic
escape hatch.

.EXAMPLE
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
  -File .\devtools\Invoke-WindowsBuild.ps1 `
  -Mode Isolated -Ref branch:feature/40-windows-ssh-build `
  -GamePath 'D:\path\to\7 Days To Die' -OutputRoot 'C:\builds\VTT'

.EXAMPLE
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
  -File .\devtools\Invoke-WindowsBuild.ps1 `
  -Mode Prepared -RepositoryPath 'C:\src\VisitedTraderTeleport' `
  -Ref commit:0123456789abcdef0123456789abcdef01234567 -DryRun

.NOTES
Exit codes: 0 success; 2 invalid input/path/executable; 3 remote ref, network,
or unstable-ref failure; 4 disposable workspace failure; 5 game-reference
failure; 6 Prepared checkout/lock failure; 7 build failure; 8 package-check
failure; 9 artifact failure; 10 timeout; 99 unexpected failure.

The final stdout line is: VTT_BUILD_RESULT <single-line JSON>.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Ref,

    [ValidateSet('Isolated', 'Prepared')]
    [string]$Mode = 'Isolated',
    [string]$RemoteUrl = $(if ($env:VTT_BUILD_REMOTE_URL) { $env:VTT_BUILD_REMOTE_URL } else { 'https://github.com/WhiteAnthrax/VisitedTraderTeleport.git' }),
    [string]$RepositoryPath = $env:VTT_BUILD_REPOSITORY_PATH,
    [string]$GamePath = $env:VTT_GAME_PATH,
    [string]$OutputRoot = $env:VTT_BUILD_ROOT,
    [string]$WorkRoot = $env:VTT_BUILD_WORK_ROOT,
    [string]$DotNetPath = $(if ($env:VTT_DOTNET_PATH) { $env:VTT_DOTNET_PATH } else { 'dotnet' }),
    [string]$GitPath = $(if ($env:VTT_GIT_PATH) { $env:VTT_GIT_PATH } else { 'git' }),
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 1800,
    [switch]$DryRun,
    [switch]$KeepWorkspace,
    [switch]$SkipPackageChecks
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:LogPath = $null
$script:RunId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$script:StartUtc = [DateTime]::UtcNow
$script:OverallDeadlineUtc = $script:StartUtc.AddSeconds($TimeoutSeconds)
$script:ResolvedCommit = $null
$script:RemoteAttempt = 0
$script:TimedOut = $false
$script:FailurePhase = $null
$script:ArtifactPath = $null
$script:ArtifactHash = $null
$script:WorkspacePath = $null
$script:OwnedWorkspaces = New-Object System.Collections.ArrayList
$script:PreparedLockStream = $null
$script:PreparedLockPath = $null
$script:Outcome = 'failure'
$script:OutcomeExitCode = 99
$script:OutcomeMessage = 'Unexpected failure.'
$script:OutcomePhase = 'startup'

$script:ProjectRelativePath = 'src\VisitedTraderTeleport\VisitedTraderTeleport.csproj'
$script:RemoteAttemptBudgetSeconds = 120
$script:WorkspaceBudgetSeconds = 300
$script:BuildBudgetSeconds = 900
$script:ChecksBudgetSeconds = 600
$script:MaxNetworkAttempts = 3

function Write-BuildLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$Level = 'INFO'
    )

    $timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
    $line = '{0} [{1}] {2}' -f $timestamp, $Level, $Message
    [Console]::Out.WriteLine($line)
    if ($script:LogPath) {
        [System.IO.File]::AppendAllText($script:LogPath, $line + [Environment]::NewLine, $script:Utf8NoBom)
    }
}

function Protect-LogText {
    param(
        [AllowNull()][string]$Text,
        [string[]]$SensitiveValues
    )

    $safe = if ($null -eq $Text) { '' } else { $Text }
    foreach ($sensitive in @($SensitiveValues)) {
        if (-not [string]::IsNullOrEmpty($sensitive)) {
            $safe = $safe.Replace($sensitive, '<redacted-remote>')
        }
    }
    return $safe
}

function Fail-Build {
    param(
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$Message,
        [switch]$TimedOut
    )

    Write-BuildLog -Level 'ERROR' -Message $Message
    $exception = New-Object System.Exception($Message)
    $exception.Data['VttBuildFailure'] = $true
    $exception.Data['ExitCode'] = $ExitCode
    $exception.Data['Phase'] = $Phase
    $exception.Data['TimedOut'] = [bool]$TimedOut
    throw $exception
}

function Assert-SafeInput {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][string]$Value,
        [switch]$Required,
        [switch]$RejectLeadingDash
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        if ($Required) {
            Fail-Build -ExitCode 2 -Phase 'validate' -Message ("{0} is required." -f $Name)
        }
        return
    }
    if ($RejectLeadingDash -and $Value.StartsWith('-')) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message ("{0} must not start with '-'." -f $Name)
    }
    foreach ($character in $Value.ToCharArray()) {
        if ([char]::IsControl($character)) {
            Fail-Build -ExitCode 2 -Phase 'validate' -Message ("{0} must not contain control characters." -f $Name)
        }
    }
}

function Resolve-Executable {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $resolved = Get-Command -Name $Command -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $resolved) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message ("Cannot find {0} executable '{1}'." -f $Description, $Command)
    }
    return $resolved.Source
}

function Assert-OverallDeadline {
    param([string]$Phase)

    if ([DateTime]::UtcNow -ge $script:OverallDeadlineUtc) {
        $script:TimedOut = $true
        Fail-Build -ExitCode 10 -Phase $Phase -Message ("Overall timeout of {0} seconds expired." -f $TimeoutSeconds) -TimedOut
    }
}

function Get-PhaseDeadline {
    param([int]$BudgetSeconds)

    $phaseDeadline = [DateTime]::UtcNow.AddSeconds($BudgetSeconds)
    if ($phaseDeadline -gt $script:OverallDeadlineUtc) {
        return $script:OverallDeadlineUtc
    }
    return $phaseDeadline
}

function ConvertTo-NativeArgument {
    param([AllowEmptyString()][string]$Value)

    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    $builder = New-Object System.Text.StringBuilder
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq [char]92) {
            $backslashes++
            continue
        }
        if ($character -eq [char]34) {
            [void]$builder.Append(('\' * (($backslashes * 2) + 1)))
            [void]$builder.Append('"')
            $backslashes = 0
            continue
        }
        if ($backslashes -gt 0) {
            [void]$builder.Append(('\' * $backslashes))
            $backslashes = 0
        }
        [void]$builder.Append($character)
    }
    if ($backslashes -gt 0) {
        [void]$builder.Append(('\' * ($backslashes * 2)))
    }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Stop-NativeProcessTree {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process)

    Write-BuildLog -Level 'WARN' -Message ('Terminating process tree rooted at PID {0}.' -f $Process.Id)
    if ($env:OS -eq 'Windows_NT') {
        $taskkill = Join-Path $env:SystemRoot 'System32\taskkill.exe'
        try {
            $killOutput = @(& $taskkill '/PID' $Process.Id.ToString() '/T' '/F' 2>&1)
            foreach ($line in $killOutput) {
                Write-BuildLog -Level 'TOOL' -Message ('taskkill: {0}' -f $line.ToString())
            }
        }
        catch {
            Write-BuildLog -Level 'WARN' -Message ('taskkill failed: {0}' -f $_.Exception.Message)
        }
    }
    else {
        try {
            $Process.Kill($true)
        }
        catch {
            try { $Process.Kill() } catch { }
        }
    }

    try {
        if (-not $Process.WaitForExit(5000)) {
            try { $Process.Kill() } catch { }
        }
    }
    catch { }
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][DateTime]$DeadlineUtc,
        [string]$WorkingDirectory,
        [string[]]$SensitiveValues
    )

    Assert-OverallDeadline -Phase $Phase
    if ([DateTime]::UtcNow -ge $DeadlineUtc) {
        $scope = if ([DateTime]::UtcNow -ge $script:OverallDeadlineUtc) { 'overall' } else { 'phase' }
        Write-BuildLog -Level 'ERROR' -Message ('TIMEOUT {0}: scope={1}; command was not started.' -f $Description, $scope)
        return [PSCustomObject]@{
            ExitCode = -1; StdOut = ''; StdErr = ''; TimedOut = $true; TimeoutScope = $scope; StartError = $null
        }
    }
    $displayArguments = @($ArgumentList | ForEach-Object {
        $display = Protect-LogText -Text $_ -SensitiveValues $SensitiveValues
        ConvertTo-NativeArgument -Value $display
    }) -join ' '
    Write-BuildLog -Message ('RUN {0}: {1} {2}' -f $Description, $FilePath, $displayArguments)

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $startInfo.WorkingDirectory = $WorkingDirectory
    }

    $argumentListProperty = $startInfo.PSObject.Properties['ArgumentList']
    if ($null -ne $argumentListProperty) {
        foreach ($argument in $ArgumentList) {
            [void]$startInfo.ArgumentList.Add($argument)
        }
    }
    else {
        $startInfo.Arguments = (@($ArgumentList | ForEach-Object { ConvertTo-NativeArgument -Value $_ }) -join ' ')
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    $stdout = ''
    $stderr = ''
    $exitCode = -1
    $timedOut = $false
    $timeoutScope = $null
    $startError = $null

    try {
        if (-not $process.Start()) {
            throw 'Process.Start returned false.'
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        while (-not $process.WaitForExit(200)) {
            $now = [DateTime]::UtcNow
            if ($now -ge $DeadlineUtc) {
                $timedOut = $true
                $timeoutScope = if ($now -ge $script:OverallDeadlineUtc) { 'overall' } else { 'phase' }
                Stop-NativeProcessTree -Process $process
                break
            }
        }
        if (-not $process.HasExited) {
            Stop-NativeProcessTree -Process $process
        }
        try { $process.WaitForExit() } catch { }
        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        if ($process.HasExited) {
            $exitCode = $process.ExitCode
        }
    }
    catch {
        $startError = $_.Exception.Message
        try {
            if (-not $process.HasExited) { Stop-NativeProcessTree -Process $process }
        }
        catch { }
    }
    finally {
        foreach ($line in @($stdout -split '\r?\n')) {
            if ($line.Length -gt 0) {
                Write-BuildLog -Level 'TOOL' -Message (Protect-LogText -Text $line -SensitiveValues $SensitiveValues)
            }
        }
        foreach ($line in @($stderr -split '\r?\n')) {
            if ($line.Length -gt 0) {
                Write-BuildLog -Level 'TOOL' -Message (Protect-LogText -Text $line -SensitiveValues $SensitiveValues)
            }
        }
        $process.Dispose()
    }

    if ($timedOut) {
        Write-BuildLog -Level 'ERROR' -Message ('TIMEOUT {0}: scope={1}' -f $Description, $timeoutScope)
    }
    elseif ($startError) {
        Write-BuildLog -Level 'ERROR' -Message ('START_FAILED {0}: {1}' -f $Description, $startError)
    }
    else {
        Write-BuildLog -Message ('EXIT {0}: {1}' -f $Description, $exitCode)
    }

    return [PSCustomObject]@{
        ExitCode = $exitCode
        StdOut = $stdout
        StdErr = $stderr
        TimedOut = $timedOut
        TimeoutScope = $timeoutScope
        StartError = $startError
    }
}

function Assert-CommandSucceeded {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Result.TimedOut) {
        $script:TimedOut = $true
        Fail-Build -ExitCode 10 -Phase $Phase -Message ("{0} ({1} timeout)." -f $Message, $Result.TimeoutScope) -TimedOut
    }
    if ($Result.StartError) {
        Fail-Build -ExitCode $ExitCode -Phase $Phase -Message ("{0}: {1}" -f $Message, $Result.StartError)
    }
    if ($Result.ExitCode -ne 0) {
        Fail-Build -ExitCode $ExitCode -Phase $Phase -Message ("{0} (exit code {1})." -f $Message, $Result.ExitCode)
    }
}

function Test-PathIsInside {
    param(
        [Parameter(Mandatory = $true)][string]$Candidate,
        [Parameter(Mandatory = $true)][string]$Container
    )

    $candidateFull = [System.IO.Path]::GetFullPath($Candidate).TrimEnd('\', '/')
    $containerFull = [System.IO.Path]::GetFullPath($Container).TrimEnd('\', '/')
    $prefix = $containerFull + [System.IO.Path]::DirectorySeparatorChar
    return $candidateFull.Equals($containerFull, [StringComparison]::OrdinalIgnoreCase) -or
        $candidateFull.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
}

function New-OwnedWorkspace {
    param(
        [Parameter(Mandatory = $true)][string]$Kind,
        [switch]$Keep
    )

    Assert-OverallDeadline -Phase 'workspace'
    try {
        [void][System.IO.Directory]::CreateDirectory($WorkRoot)
        $leaf = '{0}-{1}-{2}' -f $Kind, $script:RunId, $PID
        $path = Join-Path $WorkRoot $leaf
        if (Test-Path -LiteralPath $path) {
            Fail-Build -ExitCode 4 -Phase 'workspace' -Message ("Refusing to reuse existing workspace '{0}'." -f $path)
        }
        [void][System.IO.Directory]::CreateDirectory($path)
    }
    catch {
        if ($_.Exception.Data['VttBuildFailure']) { throw }
        Fail-Build -ExitCode 4 -Phase 'workspace' -Message ('Cannot create disposable workspace: {0}' -f $_.Exception.Message)
    }
    [void]$script:OwnedWorkspaces.Add([PSCustomObject]@{
        Path = $path
        Keep = [bool]$Keep
        Leaf = $leaf
    })
    Write-BuildLog -Message ('Created script-owned {0} workspace: {1}' -f $Kind, $path)
    return $path
}

function Remove-OwnedWorkspaces {
    $cleanupError = $null
    foreach ($workspace in @($script:OwnedWorkspaces | Select-Object -Last 100 | Sort-Object { $_.Path.Length } -Descending)) {
        if ($workspace.Keep) {
            Write-BuildLog -Level 'WARN' -Message ('Keeping workspace by explicit request: {0}' -f $workspace.Path)
            continue
        }
        try {
            $expected = Join-Path $WorkRoot $workspace.Leaf
            if (-not $workspace.Path.Equals($expected, [StringComparison]::OrdinalIgnoreCase) -or
                -not (Test-PathIsInside -Candidate $workspace.Path -Container $WorkRoot)) {
                throw ("Workspace cleanup guard rejected '{0}'." -f $workspace.Path)
            }
            if (Test-Path -LiteralPath $workspace.Path) {
                Remove-Item -LiteralPath $workspace.Path -Recurse -Force
                Write-BuildLog -Message ('Removed script-owned workspace: {0}' -f $workspace.Path)
            }
        }
        catch {
            Write-BuildLog -Level 'ERROR' -Message ('Workspace cleanup failed for {0}: {1}' -f $workspace.Path, $_.Exception.Message)
            if (-not $cleanupError) { $cleanupError = $_.Exception.Message }
        }
    }
    return $cleanupError
}

function Get-CanonicalPreparedRepository {
    if (-not (Test-Path -LiteralPath $RepositoryPath -PathType Container)) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message ("RepositoryPath does not exist: '{0}'." -f $RepositoryPath)
    }
    $result = Invoke-NativeCommand -FilePath $GitPath -ArgumentList @(
        '-C', $RepositoryPath, 'rev-parse', '--show-toplevel'
    ) -Description 'locate Prepared repository root' -Phase 'validate' -DeadlineUtc (Get-PhaseDeadline -BudgetSeconds 60)
    Assert-CommandSucceeded -Result $result -ExitCode 2 -Phase 'validate' -Message 'RepositoryPath is not a usable Git checkout'
    $topLevel = @($result.StdOut -split '\r?\n' | Where-Object { $_.Length -gt 0 } | Select-Object -Last 1)
    if ($topLevel.Count -ne 1 -or -not (Test-Path -LiteralPath $topLevel[0] -PathType Container)) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message 'Git did not return a valid Prepared repository root.'
    }
    return (Resolve-Path -LiteralPath $topLevel[0]).Path.TrimEnd('\', '/')
}

function Get-PreparedLockHash {
    param([string]$CanonicalPath)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($CanonicalPath.ToLowerInvariant())
        $hash = $sha.ComputeHash($bytes)
        $hex = -join @($hash | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
    }
    return $hex
}

function Acquire-PreparedLock {
    param([string]$CanonicalPath)

    $hash = Get-PreparedLockHash -CanonicalPath $CanonicalPath
    try {
        $lockBase = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { [System.IO.Path]::GetTempPath() }
        $lockDirectory = Join-Path $lockBase 'VisitedTraderTeleport\locks'
        [void][System.IO.Directory]::CreateDirectory($lockDirectory)
        $script:PreparedLockPath = Join-Path $lockDirectory ($hash + '.lock')
        $script:PreparedLockStream = [System.IO.File]::Open(
            $script:PreparedLockPath,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
    }
    catch {
        $script:PreparedLockStream = $null
        Fail-Build -ExitCode 6 -Phase 'prepared-lock' -Message (
            'Another build holds the exclusive lock for this Prepared checkout, or the lock file cannot be opened.'
        )
    }
    Write-BuildLog -Message ('Acquired Prepared checkout lock keyed by canonical path hash: {0}' -f $hash)
}

function Release-PreparedLock {
    if ($script:PreparedLockStream) {
        try {
            $script:PreparedLockStream.Dispose()
            Write-BuildLog -Message 'Released Prepared checkout lock.'
        }
        catch {
            Write-BuildLog -Level 'WARN' -Message ('Prepared lock release failed: {0}' -f $_.Exception.Message)
        }
        finally {
            $script:PreparedLockStream = $null
        }
    }
}

function Get-GitSingleLine {
    param(
        [string]$Repository,
        [string[]]$Arguments,
        [string]$Description,
        [int]$FailureExitCode,
        [string]$Phase
    )

    $result = Invoke-NativeCommand -FilePath $GitPath -ArgumentList (@('-C', $Repository) + $Arguments) `
        -Description $Description -Phase $Phase -DeadlineUtc (Get-PhaseDeadline -BudgetSeconds 60)
    Assert-CommandSucceeded -Result $result -ExitCode $FailureExitCode -Phase $Phase -Message ($Description + ' failed')
    $lines = @($result.StdOut -split '\r?\n' | Where-Object { $_.Length -gt 0 })
    if ($lines.Count -ne 1) {
        Fail-Build -ExitCode $FailureExitCode -Phase $Phase -Message ($Description + ' returned an unexpected result.')
    }
    return $lines[0].Trim()
}

function Assert-PreparedState {
    param(
        [string]$Repository,
        [AllowNull()][string]$ExpectedCommit,
        [string]$Label
    )

    $statusResult = Invoke-NativeCommand -FilePath $GitPath -ArgumentList @(
        '-C', $Repository, 'status', '--porcelain=v1', '--untracked-files=all'
    ) -Description ('Prepared checkout status: ' + $Label) -Phase 'prepared-check' -DeadlineUtc (Get-PhaseDeadline -BudgetSeconds 60)
    Assert-CommandSucceeded -Result $statusResult -ExitCode 6 -Phase 'prepared-check' -Message ('Prepared checkout status failed: ' + $Label)
    if (-not [string]::IsNullOrEmpty($statusResult.StdOut.Trim())) {
        Fail-Build -ExitCode 6 -Phase 'prepared-check' -Message ('Prepared checkout has staged, unstaged, or untracked changes: ' + $Label)
    }

    if (-not [string]::IsNullOrEmpty($ExpectedCommit)) {
        $head = Get-GitSingleLine -Repository $Repository -Arguments @('rev-parse', '--verify', 'HEAD^{commit}') `
            -Description ('Prepared HEAD verification: ' + $Label) -FailureExitCode 6 -Phase 'prepared-check'
        if (-not $head.Equals($ExpectedCommit, [StringComparison]::OrdinalIgnoreCase)) {
            Fail-Build -ExitCode 6 -Phase 'prepared-check' -Message (
                "Prepared HEAD is {0}, but the remote-authoritative target is {1}; update the checkout outside this script." -f $head, $ExpectedCommit
            )
        }
    }
}

function Get-RefRequest {
    $type = $null
    $name = $null
    if ($Ref.StartsWith('branch:', [StringComparison]::Ordinal)) {
        $type = 'branch'
        $name = $Ref.Substring(7)
    }
    elseif ($Ref.StartsWith('tag:', [StringComparison]::Ordinal)) {
        $type = 'tag'
        $name = $Ref.Substring(4)
    }
    elseif ($Ref.StartsWith('commit:', [StringComparison]::Ordinal)) {
        $type = 'commit'
        $name = $Ref.Substring(7)
    }
    elseif ($Ref -cmatch '^[0-9a-fA-F]{40}$') {
        $type = 'commit'
        $name = $Ref
    }
    else {
        $type = 'unprefixed'
        $name = $Ref
    }

    Assert-SafeInput -Name 'ref name' -Value $name -Required -RejectLeadingDash
    if ($type -eq 'commit' -and $name -cnotmatch '^[0-9a-fA-F]{40}$') {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message 'commit: refs require exactly 40 hexadecimal characters.'
    }

    if ($type -ne 'commit') {
        $formatResult = Invoke-NativeCommand -FilePath $GitPath -ArgumentList @(
            'check-ref-format', ('refs/heads/' + $name)
        ) -Description 'validate ref name' -Phase 'validate' -DeadlineUtc (Get-PhaseDeadline -BudgetSeconds 30)
        if ($formatResult.TimedOut) {
            $script:TimedOut = $true
            Fail-Build -ExitCode 10 -Phase 'validate' -Message 'Git ref-name validation timed out.' -TimedOut
        }
        if ($formatResult.StartError -or $formatResult.ExitCode -ne 0) {
            Fail-Build -ExitCode 2 -Phase 'validate' -Message ("Invalid branch/tag ref name '{0}'." -f $name)
        }
    }

    return [PSCustomObject]@{ Type = $type; Name = $name }
}

function Get-RemoteAdvertisement {
    param(
        [Parameter(Mandatory = $true)]$Request,
        [Parameter(Mandatory = $true)][int]$Attempt,
        [Parameter(Mandatory = $true)][DateTime]$AttemptDeadline
    )

    $patterns = @()
    if ($Request.Type -eq 'branch') {
        $patterns = @(('refs/heads/' + $Request.Name))
    }
    elseif ($Request.Type -eq 'tag') {
        $patterns = @(
            ('refs/tags/' + $Request.Name),
            ('refs/tags/' + $Request.Name + '^{}')
        )
    }
    elseif ($Request.Type -eq 'unprefixed') {
        $patterns = @(
            ('refs/heads/' + $Request.Name),
            ('refs/tags/' + $Request.Name),
            ('refs/tags/' + $Request.Name + '^{}')
        )
    }
    else {
        $patterns = @('HEAD')
    }

    $arguments = @('-c', 'credential.interactive=false', 'ls-remote', $RemoteUrl) + $patterns
    $result = Invoke-NativeCommand -FilePath $GitPath -ArgumentList $arguments `
        -Description ('remote ref query attempt {0}' -f $Attempt) -Phase 'resolve' `
        -DeadlineUtc $AttemptDeadline -SensitiveValues @($RemoteUrl)
    if ($result.TimedOut -or $result.StartError -or $result.ExitCode -ne 0) {
        return [PSCustomObject]@{ Success = $false; Result = $result }
    }

    if ($Request.Type -eq 'commit') {
        return [PSCustomObject]@{
            Success = $true
            Commit = $Request.Name.ToLowerInvariant()
            FetchSpec = $Request.Name.ToLowerInvariant()
            RefKind = 'commit'
            Result = $result
        }
    }

    $advertised = @{}
    foreach ($line in @($result.StdOut -split '\r?\n')) {
        if ($line -match '^([0-9a-fA-F]{40})\s+(.+)$') {
            $advertised[$Matches[2]] = $Matches[1].ToLowerInvariant()
        }
    }
    $branchRef = 'refs/heads/' + $Request.Name
    $tagRef = 'refs/tags/' + $Request.Name
    $peeledTagRef = $tagRef + '^{}'
    $hasBranch = $advertised.ContainsKey($branchRef)
    $hasTag = $advertised.ContainsKey($tagRef)

    if ($Request.Type -eq 'unprefixed' -and $hasBranch -and $hasTag) {
        Fail-Build -ExitCode 3 -Phase 'resolve' -Message (
            "Remote ref '{0}' is ambiguous: both branch and tag exist; use branch:{0} or tag:{0}." -f $Request.Name
        )
    }
    if (($Request.Type -eq 'branch' -or $Request.Type -eq 'unprefixed') -and $hasBranch) {
        return [PSCustomObject]@{
            Success = $true; Commit = $advertised[$branchRef]; FetchSpec = $branchRef; RefKind = 'branch'; Result = $result
        }
    }
    if (($Request.Type -eq 'tag' -or $Request.Type -eq 'unprefixed') -and $hasTag) {
        $commit = if ($advertised.ContainsKey($peeledTagRef)) { $advertised[$peeledTagRef] } else { $advertised[$tagRef] }
        return [PSCustomObject]@{
            Success = $true; Commit = $commit; FetchSpec = $tagRef; RefKind = 'tag'; Result = $result
        }
    }

    Fail-Build -ExitCode 3 -Phase 'resolve' -Message ("Remote ref '{0}' was not found." -f $Ref)
}

function Wait-NetworkRetry {
    param([int]$Attempt)

    Assert-OverallDeadline -Phase 'resolve'
    $milliseconds = [Math]::Min(3000, $Attempt * 1000)
    $remaining = [int][Math]::Floor(($script:OverallDeadlineUtc - [DateTime]::UtcNow).TotalMilliseconds)
    if ($remaining -le 0) {
        Assert-OverallDeadline -Phase 'resolve'
    }
    Start-Sleep -Milliseconds ([Math]::Min($milliseconds, $remaining))
    Assert-OverallDeadline -Phase 'resolve'
}

function Resolve-AndFetchRemoteTarget {
    param(
        [Parameter(Mandatory = $true)]$Request,
        [Parameter(Mandatory = $true)][string]$RemoteWorkspace
    )

    $lastTimedOut = $false
    $sawMovingRef = $false
    $lastFailure = 'Remote query or fetch failed.'
    $lastPhase = 'resolve'

    for ($attempt = 1; $attempt -le $script:MaxNetworkAttempts; $attempt++) {
        $script:RemoteAttempt = $attempt
        Assert-OverallDeadline -Phase 'resolve'
        $attemptDeadline = Get-PhaseDeadline -BudgetSeconds $script:RemoteAttemptBudgetSeconds
        $advertisement = Get-RemoteAdvertisement -Request $Request -Attempt $attempt -AttemptDeadline $attemptDeadline
        if (-not $advertisement.Success) {
            $lastTimedOut = [bool]$advertisement.Result.TimedOut
            $lastFailure = 'Remote ref query failed.'
            $lastPhase = 'resolve'
        }
        else {
            Write-BuildLog -Message ('Remote advertised {0} {1} at commit {2} (attempt {3}).' -f `
                $advertisement.RefKind, $Ref, $advertisement.Commit, $attempt)
            $fetchArguments = @(
                '-c', 'credential.interactive=false', '-C', $RemoteWorkspace,
                'fetch', '--no-tags', '--depth=1', '--no-recurse-submodules', $RemoteUrl, $advertisement.FetchSpec
            )
            $fetchResult = Invoke-NativeCommand -FilePath $GitPath -ArgumentList $fetchArguments `
                -Description ('bounded remote fetch attempt {0}' -f $attempt) -Phase 'fetch' `
                -DeadlineUtc $attemptDeadline -SensitiveValues @($RemoteUrl)
            if ($fetchResult.TimedOut -or $fetchResult.StartError -or $fetchResult.ExitCode -ne 0) {
                $lastTimedOut = [bool]$fetchResult.TimedOut
                $lastFailure = 'Bounded remote fetch failed.'
                $lastPhase = 'fetch'
            }
            else {
                $verifyResult = Invoke-NativeCommand -FilePath $GitPath -ArgumentList @(
                    '-C', $RemoteWorkspace, 'rev-parse', '--verify', 'FETCH_HEAD^{commit}'
                ) -Description 'verify fetched commit' -Phase 'fetch' -DeadlineUtc $attemptDeadline
                if ($verifyResult.TimedOut) {
                    $lastTimedOut = $true
                    $lastFailure = 'Fetched commit verification timed out.'
                    $lastPhase = 'fetch'
                }
                elseif ($verifyResult.StartError -or $verifyResult.ExitCode -ne 0) {
                    $lastFailure = 'Fetched object is not a commit.'
                    $lastPhase = 'fetch'
                }
                else {
                    $fetchedCommit = @($verifyResult.StdOut -split '\r?\n' | Where-Object { $_ -cmatch '^[0-9a-fA-F]{40}$' } | Select-Object -Last 1)
                    if ($fetchedCommit.Count -eq 1 -and $fetchedCommit[0].Equals($advertisement.Commit, [StringComparison]::OrdinalIgnoreCase)) {
                        $script:ResolvedCommit = $advertisement.Commit
                        return $advertisement
                    }
                    $sawMovingRef = $true
                    $lastFailure = 'Remote ref moved between ls-remote and fetch.'
                    $lastPhase = 'fetch'
                    Write-BuildLog -Level 'WARN' -Message ('Advertised commit {0} did not match fetched commit {1}; retrying pair.' -f `
                        $advertisement.Commit, $(if ($fetchedCommit.Count) { $fetchedCommit[0] } else { '<none>' }))
                }
            }
        }

        if ($attempt -lt $script:MaxNetworkAttempts) {
            Write-BuildLog -Level 'WARN' -Message ('{0} Retrying idempotent remote operation ({1}/{2}).' -f `
                $lastFailure, ($attempt + 1), $script:MaxNetworkAttempts)
            Wait-NetworkRetry -Attempt $attempt
        }
    }

    if ($sawMovingRef) {
        Fail-Build -ExitCode 3 -Phase 'fetch' -Message ('Unstable ref after {0} resolve/fetch attempts.' -f $script:MaxNetworkAttempts)
    }
    if ($lastTimedOut) {
        $script:TimedOut = $true
        Fail-Build -ExitCode 10 -Phase $lastPhase -Message ($lastFailure + ' Retry limit exhausted after a timeout.') -TimedOut
    }
    Fail-Build -ExitCode 3 -Phase $lastPhase -Message ($lastFailure + ' Retry limit exhausted.')
}

function Initialize-GitWorkspace {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][DateTime]$WorkspaceDeadline
    )

    $result = Invoke-NativeCommand -FilePath $GitPath -ArgumentList @('-C', $Path, 'init', '--quiet') `
        -Description 'initialize disposable Git repository' -Phase 'workspace' -DeadlineUtc $WorkspaceDeadline
    Assert-CommandSucceeded -Result $result -ExitCode 4 -Phase 'workspace' -Message 'Disposable Git repository initialization failed'
}

function Checkout-IsolatedCommit {
    param(
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][DateTime]$WorkspaceDeadline
    )

    $checkout = Invoke-NativeCommand -FilePath $GitPath -ArgumentList @(
        '-C', $Repository, 'checkout', '--detach', 'FETCH_HEAD'
    ) -Description 'checkout isolated target commit' -Phase 'workspace' -DeadlineUtc $WorkspaceDeadline
    Assert-CommandSucceeded -Result $checkout -ExitCode 4 -Phase 'workspace' -Message 'Isolated detached checkout failed'
    $head = Get-GitSingleLine -Repository $Repository -Arguments @('rev-parse', '--verify', 'HEAD^{commit}') `
        -Description 'verify isolated HEAD' -FailureExitCode 4 -Phase 'workspace'
    if (-not $head.Equals($script:ResolvedCommit, [StringComparison]::OrdinalIgnoreCase)) {
        Fail-Build -ExitCode 4 -Phase 'workspace' -Message (
            'Isolated checkout commit does not match the remotely verified commit.'
        )
    }
}

function Get-ReferencePlan {
    param([Parameter(Mandatory = $true)][string]$BuildRepository)

    Assert-OverallDeadline -Phase 'references'
    $projectPath = Join-Path $BuildRepository $script:ProjectRelativePath
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        Fail-Build -ExitCode 5 -Phase 'references' -Message ("Target project file is missing: '{0}'." -f $projectPath)
    }
    try {
        [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    }
    catch {
        Fail-Build -ExitCode 5 -Phase 'references' -Message ('Cannot read target project references: {0}' -f $_.Exception.Message)
    }

    $plan = @()
    $seen = @{}
    foreach ($node in @($project.SelectNodes('//Reference/HintPath'))) {
        $hintPath = $node.InnerText.Replace('/', '\')
        $sourceDirectory = $null
        $destinationDirectory = $null
        if ($hintPath -match '(^|\\)refs\\managed\\([^\\]+)$') {
            $fileName = $Matches[2]
            $sourceDirectory = Join-Path $GamePath '7DaysToDie_Data\Managed'
            $destinationDirectory = Join-Path $BuildRepository 'refs\managed'
        }
        elseif ($hintPath -match '(^|\\)refs\\harmony\\([^\\]+)$') {
            $fileName = $Matches[2]
            $sourceDirectory = Join-Path $GamePath 'Mods\0_TFP_Harmony'
            $destinationDirectory = Join-Path $BuildRepository 'refs\harmony'
        }
        elseif ($hintPath -match '(^|\\)refs\\') {
            Fail-Build -ExitCode 5 -Phase 'references' -Message ("Unsupported local reference HintPath: '{0}'." -f $hintPath)
        }
        else {
            continue
        }

        $key = ($destinationDirectory + '\' + $fileName).ToLowerInvariant()
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = $true
            $source = Join-Path $sourceDirectory $fileName
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                Fail-Build -ExitCode 5 -Phase 'references' -Message ("Required game reference is missing: '{0}'." -f $source)
            }
            $plan += [PSCustomObject]@{
                Name = $fileName
                Source = (Resolve-Path -LiteralPath $source).Path
                DestinationDirectory = $destinationDirectory
                Destination = Join-Path $destinationDirectory $fileName
            }
        }
    }
    if ($plan.Count -eq 0) {
        Fail-Build -ExitCode 5 -Phase 'references' -Message 'Target project declares no supported refs/ HintPaths.'
    }
    Write-BuildLog -Message ('Validated {0} required game reference HintPaths from the target project.' -f $plan.Count)
    return ,$plan
}

function Copy-ReferencePlan {
    param([Parameter(Mandatory = $true)][array]$Plan)

    try {
        foreach ($reference in $Plan) {
            Assert-OverallDeadline -Phase 'references'
            [void][System.IO.Directory]::CreateDirectory($reference.DestinationDirectory)
            Copy-Item -LiteralPath $reference.Source -Destination $reference.Destination -Force
            Write-BuildLog -Message ('Copied game reference: {0}' -f $reference.Name)
        }
    }
    catch {
        Fail-Build -ExitCode 5 -Phase 'references' -Message ('Game reference copy failed: {0}' -f $_.Exception.Message)
    }
}

function Collect-Artifact {
    param([Parameter(Mandatory = $true)][string]$BuildRepository)

    Assert-OverallDeadline -Phase 'artifact'
    try {
        $modInfoPath = Join-Path $BuildRepository 'mod\VisitedTraderTeleport\ModInfo.xml'
        [xml]$modInfo = Get-Content -LiteralPath $modInfoPath -Raw
        $version = $modInfo.DocumentElement.Version.value
        if ([string]::IsNullOrWhiteSpace($version)) {
            Fail-Build -ExitCode 9 -Phase 'artifact' -Message 'ModInfo.xml has no Version value.'
        }
        $packageName = 'VisitedTraderTeleport-{0}.zip' -f $version
        $expectedPackage = Join-Path (Join-Path $BuildRepository 'dist') $packageName
        if (-not (Test-Path -LiteralPath $expectedPackage -PathType Leaf)) {
            Fail-Build -ExitCode 9 -Phase 'artifact' -Message ("Expected package was not produced: '{0}'." -f $expectedPackage)
        }
        $script:ArtifactPath = Join-Path $script:ArtifactDirectory $packageName
        Copy-Item -LiteralPath $expectedPackage -Destination $script:ArtifactPath -Force
        $script:ArtifactHash = (Get-FileHash -LiteralPath $script:ArtifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-BuildLog -Message ('ARTIFACT={0}' -f $script:ArtifactPath)
        Write-BuildLog -Message ('ARTIFACT_SHA256={0}' -f $script:ArtifactHash)
        return $version
    }
    catch {
        if ($_.Exception.Data['VttBuildFailure']) { throw }
        Fail-Build -ExitCode 9 -Phase 'artifact' -Message ('Artifact collection failed: {0}' -f $_.Exception.Message)
    }
}

function Write-TerminalResult {
    $artifact = $null
    if ($script:ArtifactPath) {
        $artifact = [ordered]@{ path = $script:ArtifactPath; sha256 = $script:ArtifactHash }
    }
    $result = [ordered]@{
        result = $script:Outcome
        exitCode = $script:OutcomeExitCode
        message = $script:OutcomeMessage
        phase = $script:OutcomePhase
        mode = $Mode
        dryRun = [bool]$DryRun
        requestedRef = $Ref
        resolvedCommit = $script:ResolvedCommit
        attempt = $script:RemoteAttempt
        timedOut = [bool]$script:TimedOut
        runId = $script:RunId
        logPath = $script:LogPath
        workspacePath = $script:WorkspacePath
        workspaceKept = [bool]($Mode -eq 'Isolated' -and $KeepWorkspace -and $script:WorkspacePath -and
            (Test-Path -LiteralPath $script:WorkspacePath -PathType Container))
        artifact = $artifact
    }
    [Console]::Out.WriteLine('VTT_BUILD_RESULT ' + ($result | ConvertTo-Json -Compress -Depth 5))
}

try {
    Assert-SafeInput -Name 'Ref' -Value $Ref -Required -RejectLeadingDash
    Assert-SafeInput -Name 'RemoteUrl' -Value $RemoteUrl -Required -RejectLeadingDash
    Assert-SafeInput -Name 'RepositoryPath' -Value $RepositoryPath -RejectLeadingDash
    Assert-SafeInput -Name 'GamePath' -Value $GamePath -Required -RejectLeadingDash
    Assert-SafeInput -Name 'OutputRoot' -Value $OutputRoot -RejectLeadingDash
    Assert-SafeInput -Name 'WorkRoot' -Value $WorkRoot -RejectLeadingDash
    Assert-SafeInput -Name 'DotNetPath' -Value $DotNetPath -Required -RejectLeadingDash
    Assert-SafeInput -Name 'GitPath' -Value $GitPath -Required -RejectLeadingDash
    $remoteUri = $null
    if ([Uri]::TryCreate($RemoteUrl, [UriKind]::Absolute, [ref]$remoteUri) -and
        ($remoteUri.Scheme -eq 'http' -or $remoteUri.Scheme -eq 'https') -and
        -not [string]::IsNullOrEmpty($remoteUri.UserInfo)) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message 'RemoteUrl must not embed credentials; use the Git credential helper instead.'
    }
    if ($Mode -eq 'Prepared' -and [string]::IsNullOrWhiteSpace($RepositoryPath)) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message 'RepositoryPath is required in Prepared mode.'
    }
    if ($Mode -eq 'Isolated' -and -not [string]::IsNullOrWhiteSpace($RepositoryPath)) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message 'RepositoryPath is not used in Isolated mode; omit it or select Prepared mode.'
    }
    if ($Mode -eq 'Prepared' -and $KeepWorkspace) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message 'KeepWorkspace is valid only in Isolated mode.'
    }

    if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        $outputBase = if ($env:LOCALAPPDATA) { $env:LOCALAPPDATA } else { $env:TEMP }
        if ([string]::IsNullOrWhiteSpace($outputBase)) {
            Fail-Build -ExitCode 2 -Phase 'validate' -Message 'Set OutputRoot/VTT_BUILD_ROOT; LOCALAPPDATA and TEMP are unavailable.'
        }
        $OutputRoot = Join-Path $outputBase 'VisitedTraderTeleport\builds'
    }
    Assert-SafeInput -Name 'OutputRoot' -Value $OutputRoot -Required -RejectLeadingDash
    if ([string]::IsNullOrWhiteSpace($WorkRoot)) {
        $WorkRoot = Join-Path $OutputRoot 'workspaces'
    }
    Assert-SafeInput -Name 'WorkRoot' -Value $WorkRoot -Required -RejectLeadingDash
    try {
        $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
        $WorkRoot = [System.IO.Path]::GetFullPath($WorkRoot)
    }
    catch {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message ('OutputRoot or WorkRoot is invalid: {0}' -f $_.Exception.Message)
    }

    $GitPath = Resolve-Executable -Command $GitPath -Description 'Git'
    $DotNetPath = Resolve-Executable -Command $DotNetPath -Description '.NET'
    if (-not (Test-Path -LiteralPath $GamePath -PathType Container)) {
        Fail-Build -ExitCode 2 -Phase 'validate' -Message ("GamePath does not exist: '{0}'." -f $GamePath)
    }
    $GamePath = (Resolve-Path -LiteralPath $GamePath).Path.TrimEnd('\', '/')

    $env:GIT_TERMINAL_PROMPT = '0'
    $env:GCM_INTERACTIVE = 'Never'
    $env:SSH_ASKPASS_REQUIRE = 'never'
    $env:GIT_SSH_COMMAND = 'ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=30'

    $preparedRepository = $null
    if ($Mode -eq 'Prepared') {
        $preparedRepository = Get-CanonicalPreparedRepository
        if (Test-PathIsInside -Candidate $OutputRoot -Container $preparedRepository) {
            Fail-Build -ExitCode 2 -Phase 'validate' -Message 'OutputRoot must be outside the Prepared repository.'
        }
        if (Test-PathIsInside -Candidate $WorkRoot -Container $preparedRepository) {
            Fail-Build -ExitCode 2 -Phase 'validate' -Message 'WorkRoot must be outside the Prepared repository.'
        }
    }
    else {
        $scriptRepository = Split-Path -Parent $PSScriptRoot
        if (Test-Path -LiteralPath (Join-Path $scriptRepository 'VisitedTraderTeleport.sln') -PathType Leaf) {
            $scriptRepository = (Resolve-Path -LiteralPath $scriptRepository).Path
            if (Test-PathIsInside -Candidate $OutputRoot -Container $scriptRepository) {
                Fail-Build -ExitCode 2 -Phase 'validate' -Message 'OutputRoot must be outside the repository containing this script.'
            }
            if (Test-PathIsInside -Candidate $WorkRoot -Container $scriptRepository) {
                Fail-Build -ExitCode 2 -Phase 'validate' -Message 'WorkRoot must be outside the repository containing this script.'
            }
        }
    }

    try {
        [void][System.IO.Directory]::CreateDirectory((Join-Path $OutputRoot 'logs'))
        $script:LogPath = Join-Path (Join-Path $OutputRoot 'logs') ($script:RunId + '.log')
        [System.IO.File]::WriteAllText($script:LogPath, '', $script:Utf8NoBom)
        $script:ArtifactDirectory = Join-Path (Join-Path $OutputRoot 'artifacts') $script:RunId
        if (-not $DryRun) {
            [void][System.IO.Directory]::CreateDirectory($script:ArtifactDirectory)
        }
    }
    catch {
        $outputError = $_.Exception.Message
        $script:LogPath = $null
        Fail-Build -ExitCode 2 -Phase 'validate' -Message ('Cannot initialize OutputRoot: {0}' -f $outputError)
    }

    Write-BuildLog -Message ('Build run {0} started: mode={1} dryRun={2} timeoutSeconds={3}.' -f `
        $script:RunId, $Mode, [bool]$DryRun, $TimeoutSeconds)
    Write-BuildLog -Message ('Requested remote-authoritative ref: {0}' -f $Ref)
    Write-BuildLog -Message ('LOG_PATH={0}' -f $script:LogPath)
    Write-BuildLog -Message ('OUTPUT_ROOT={0}' -f $OutputRoot)
    Write-BuildLog -Message ('WORK_ROOT={0}' -f $WorkRoot)

    $request = Get-RefRequest
    if ($Mode -eq 'Prepared') {
        Acquire-PreparedLock -CanonicalPath $preparedRepository
        Assert-PreparedState -Repository $preparedRepository -ExpectedCommit $null -Label 'initial preflight'
    }

    $workspaceDeadline = Get-PhaseDeadline -BudgetSeconds $script:WorkspaceBudgetSeconds
    $workspaceKind = if ($Mode -eq 'Isolated') { 'workspace' } else { 'probe' }
    $keepOwnedWorkspace = $Mode -eq 'Isolated' -and $KeepWorkspace
    $remoteWorkspace = New-OwnedWorkspace -Kind $workspaceKind -Keep:$keepOwnedWorkspace
    if ($Mode -eq 'Isolated') { $script:WorkspacePath = $remoteWorkspace }
    Initialize-GitWorkspace -Path $remoteWorkspace -WorkspaceDeadline $workspaceDeadline
    $advertisement = Resolve-AndFetchRemoteTarget -Request $request -RemoteWorkspace $remoteWorkspace

    if ($Mode -eq 'Isolated') {
        Checkout-IsolatedCommit -Repository $remoteWorkspace -WorkspaceDeadline $workspaceDeadline
        $buildRepository = $remoteWorkspace
    }
    else {
        Assert-PreparedState -Repository $preparedRepository -ExpectedCommit $script:ResolvedCommit -Label 'remote target preflight'
        $buildRepository = $preparedRepository
    }

    $referencePlan = Get-ReferencePlan -BuildRepository $buildRepository
    Write-BuildLog -Message ('PLAN mode={0} resolvedCommit={1} buildRepository={2} outputRoot={3} workRoot={4}' -f `
        $Mode, $script:ResolvedCommit, $buildRepository, $OutputRoot, $WorkRoot)

    if ($DryRun) {
        if ($Mode -eq 'Prepared') {
            Assert-PreparedState -Repository $preparedRepository -ExpectedCommit $script:ResolvedCommit -Label 'DryRun completion'
        }
        $script:Outcome = 'success'
        $script:OutcomeExitCode = 0
        $script:OutcomeMessage = 'DryRun preflight passed; no references were copied and no build or package checks ran.'
        $script:OutcomePhase = 'dry-run'
    }
    else {
        Copy-ReferencePlan -Plan $referencePlan
        if ($Mode -eq 'Prepared') {
            Assert-PreparedState -Repository $preparedRepository -ExpectedCommit $script:ResolvedCommit -Label 'immediately before build'
        }

        $buildResult = Invoke-NativeCommand -FilePath $DotNetPath -ArgumentList @(
            'build', $script:ProjectRelativePath, '-c', 'Release'
        ) -Description 'Release build and packaging' -Phase 'build' `
            -DeadlineUtc (Get-PhaseDeadline -BudgetSeconds $script:BuildBudgetSeconds) -WorkingDirectory $buildRepository

        if ($Mode -eq 'Prepared') {
            if ($buildResult.TimedOut -and $buildResult.TimeoutScope -eq 'overall') {
                Write-BuildLog -Level 'WARN' -Message 'Overall deadline prevented the post-build Prepared state check.'
            }
            elseif ($buildResult.TimedOut) {
                try {
                    Assert-PreparedState -Repository $preparedRepository -ExpectedCommit $script:ResolvedCommit `
                        -Label 'immediately after timed-out build'
                }
                catch {
                    Write-BuildLog -Level 'WARN' -Message (
                        'Prepared state validation also failed after the build timeout; timeout remains the terminal result.'
                    )
                }
            }
            else {
                Assert-PreparedState -Repository $preparedRepository -ExpectedCommit $script:ResolvedCommit -Label 'immediately after build'
            }
        }
        Assert-CommandSucceeded -Result $buildResult -ExitCode 7 -Phase 'build' -Message 'Release build failed'

        if ($SkipPackageChecks) {
            Write-BuildLog -Level 'WARN' -Message 'Package checks skipped by explicit request.'
        }
        else {
            $checksResult = Invoke-NativeCommand -FilePath $DotNetPath -ArgumentList @(
                'run', '--project', 'devtools\ModChecks', '--', '--package'
            ) -Description 'package verification' -Phase 'verify' `
                -DeadlineUtc (Get-PhaseDeadline -BudgetSeconds $script:ChecksBudgetSeconds) -WorkingDirectory $buildRepository
            Assert-CommandSucceeded -Result $checksResult -ExitCode 8 -Phase 'verify' -Message 'Package verification failed'
        }
        if ($Mode -eq 'Prepared') {
            Assert-PreparedState -Repository $preparedRepository -ExpectedCommit $script:ResolvedCommit -Label 'final checkout verification'
        }

        $version = Collect-Artifact -BuildRepository $buildRepository
        $script:Outcome = 'success'
        $script:OutcomeExitCode = 0
        $script:OutcomeMessage = ('Built and verified version {0} at commit {1}.' -f $version, $script:ResolvedCommit)
        $script:OutcomePhase = 'complete'
    }
}
catch {
    if ($_.Exception.Data['VttBuildFailure']) {
        $script:OutcomeExitCode = [int]$_.Exception.Data['ExitCode']
        $script:OutcomePhase = [string]$_.Exception.Data['Phase']
        $script:OutcomeMessage = $_.Exception.Message
        $script:TimedOut = [bool]$_.Exception.Data['TimedOut']
    }
    else {
        $script:OutcomeExitCode = 99
        $script:OutcomePhase = if ($script:FailurePhase) { $script:FailurePhase } else { 'unexpected' }
        $script:OutcomeMessage = 'Unexpected failure: ' + $_.Exception.Message
        try { Write-BuildLog -Level 'ERROR' -Message $script:OutcomeMessage } catch { }
    }
}
finally {
    $cleanupError = Remove-OwnedWorkspaces
    Release-PreparedLock
    if ($cleanupError -and $script:OutcomeExitCode -eq 0) {
        $script:Outcome = 'failure'
        $script:OutcomeExitCode = 4
        $script:OutcomePhase = 'cleanup'
        $script:OutcomeMessage = 'Build completed, but disposable workspace cleanup failed: ' + $cleanupError
    }
}

try {
    if ($script:OutcomeExitCode -eq 0) {
        Write-BuildLog -Message $script:OutcomeMessage
    }
    else {
        [Console]::Error.WriteLine(('BUILD_FAILED exit_code={0} phase={1} message={2}' -f `
            $script:OutcomeExitCode, $script:OutcomePhase, $script:OutcomeMessage))
    }
}
catch {
    $script:Outcome = 'failure'
    $script:OutcomeExitCode = 99
    $script:OutcomePhase = 'terminal-output'
    $script:OutcomeMessage = 'Final result handling failed: ' + $_.Exception.Message
    try {
        [Console]::Error.WriteLine(('BUILD_FAILED exit_code=99 phase=terminal-output message={0}' -f `
            $script:OutcomeMessage))
    }
    catch { [void]$_ }
}
finally {
    try {
        Write-TerminalResult
    }
    catch {
        $script:Outcome = 'failure'
        $script:OutcomeExitCode = 99
        $script:OutcomePhase = 'terminal-output'
        $script:OutcomeMessage = 'Terminal result emission failed.'

        $fallbackMode = if ($Mode -eq 'Prepared') { 'Prepared' } else { 'Isolated' }
        $fallbackCommit = if ($script:ResolvedCommit -match '\A[0-9a-fA-F]{40}\z') {
            '"' + $script:ResolvedCommit.ToLowerInvariant() + '"'
        }
        else {
            'null'
        }
        $fallbackTimedOut = if ($script:TimedOut) { 'true' } else { 'false' }
        $fallbackResult = 'VTT_BUILD_RESULT {{"result":"failure","exitCode":99,"message":"Terminal result emission failed.","phase":"terminal-output","mode":"{0}","resolvedCommit":{1},"attempt":{2},"timedOut":{3}}}' -f `
            $fallbackMode, $fallbackCommit, [int]$script:RemoteAttempt, $fallbackTimedOut
        try { [Console]::Out.WriteLine($fallbackResult) } catch { [void]$_ }
    }
    exit $script:OutcomeExitCode
}

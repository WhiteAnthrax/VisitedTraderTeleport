<#
.SYNOPSIS
Builds and verifies a VisitedTraderTeleport package on Windows for an SSH caller.

.DESCRIPTION
Resolves -Ref to an exact commit in -RepositoryPath. By default the checkout must
already be at that commit. Use -Fetch to fetch the ref from -RemoteName and
-CheckoutRef to explicitly allow a clean checkout to move to a detached commit.

Game reference paths come from -GamePath/-ManagedPath/-HarmonyPath (or
VTT_GAME_PATH), and outputs go under -OutputRoot (or VTT_BUILD_ROOT). The script
populates the ignored refs directories, runs the existing Release build, runs
package checks unless -SkipPackageChecks is set, and copies the ZIP to the run's
artifact directory.

.EXAMPLE
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
  -File .\devtools\Invoke-WindowsBuild.ps1 `
  -Ref feature/40-windows-ssh-build -Fetch -CheckoutRef `
  -GamePath 'D:\path\to\7 Days To Die' -OutputRoot 'C:\builds\VTT'

.NOTES
Exit codes: 0 success; 2 invalid input or checkout precondition; 3 git failure;
4 reference population failure; 5 build failure; 6 package verification failure;
7 artifact collection failure; 99 unexpected failure.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Ref,

    [string]$RepositoryPath = (Split-Path -Parent $PSScriptRoot),
    [string]$GamePath = $env:VTT_GAME_PATH,
    [string]$ManagedPath,
    [string]$HarmonyPath,
    [string]$OutputRoot = $env:VTT_BUILD_ROOT,
    [string]$DotNetPath = $env:VTT_DOTNET_PATH,
    [string]$GitPath = 'git',
    [string]$RemoteName = 'origin',
    [switch]$Fetch,
    [switch]$CheckoutRef,
    [switch]$SkipPackageChecks
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:LogPath = $null
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

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

function Stop-Build {
    param(
        [Parameter(Mandatory = $true)][int]$ExitCode,
        [Parameter(Mandatory = $true)][string]$Message
    )

    Write-BuildLog -Level 'ERROR' -Message $Message
    [Console]::Error.WriteLine(('BUILD_FAILED exit_code={0} message={1}' -f $ExitCode, $Message))
    exit $ExitCode
}

function Format-CommandArgument {
    param([string]$Value)

    if ($Value -match '[\s"]') {
        return '"' + $Value.Replace('"', '\"') + '"'
    }

    return $Value
}

function Invoke-BuildCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $displayArgs = ($ArgumentList | ForEach-Object { Format-CommandArgument $_ }) -join ' '
    Write-BuildLog -Message ('RUN {0}: {1} {2}' -f $Description, $FilePath, $displayArgs)
    & $FilePath @ArgumentList 2>&1 | ForEach-Object {
        Write-BuildLog -Level 'TOOL' -Message $_.ToString()
    }
    $commandExitCode = $LASTEXITCODE
    Write-BuildLog -Message ('EXIT {0}: {1}' -f $Description, $commandExitCode)
    return $commandExitCode
}

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $displayArgs = ($ArgumentList | ForEach-Object { Format-CommandArgument $_ }) -join ' '
    Write-BuildLog -Message ('RUN {0}: {1} {2}' -f $Description, $FilePath, $displayArgs)
    $commandOutput = @(& $FilePath @ArgumentList 2>&1)
    $commandExitCode = $LASTEXITCODE
    $lines = @($commandOutput | ForEach-Object {
        $line = $_.ToString()
        Write-BuildLog -Level 'TOOL' -Message $line
        $line
    })
    Write-BuildLog -Message ('EXIT {0}: {1}' -f $Description, $commandExitCode)

    return [PSCustomObject]@{
        ExitCode = $commandExitCode
        Lines = $lines
    }
}

function Resolve-Executable {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $resolved = Get-Command -Name $Command -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $resolved) {
        Stop-Build -ExitCode 2 -Message ("Cannot find {0} executable '{1}'." -f $Description, $Command)
    }

    return $resolved.Source
}

function Get-ResolvedCommit {
    param([string]$Revision)

    $result = Invoke-CapturedCommand -FilePath $GitPath -ArgumentList @(
        '-C', $RepositoryPath, 'rev-parse', '--verify', ($Revision + '^{commit}')
    ) -Description ('resolve ref {0}' -f $Revision)
    if ($result.ExitCode -ne 0) {
        Stop-Build -ExitCode 3 -Message ("Git could not resolve ref '{0}' to a commit." -f $Revision)
    }

    $commit = $result.Lines | Where-Object { $_ -match '^[0-9a-fA-F]{40}$' } | Select-Object -Last 1
    if (-not $commit) {
        Stop-Build -ExitCode 3 -Message ("Git returned no commit hash for ref '{0}'." -f $Revision)
    }

    return $commit.ToLowerInvariant()
}

try {
    if ($Ref.StartsWith('-') -or $Ref -match '[\r\n]') {
        Stop-Build -ExitCode 2 -Message "Ref must not start with '-' or contain a newline."
    }
    if ($RemoteName.StartsWith('-') -or $RemoteName -match '[\r\n]') {
        Stop-Build -ExitCode 2 -Message "RemoteName must not start with '-' or contain a newline."
    }

    if (-not (Test-Path -LiteralPath $RepositoryPath -PathType Container)) {
        Stop-Build -ExitCode 2 -Message ("Repository path does not exist: '{0}'." -f $RepositoryPath)
    }
    $RepositoryPath = (Resolve-Path -LiteralPath $RepositoryPath).Path
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryPath 'VisitedTraderTeleport.sln') -PathType Leaf)) {
        Stop-Build -ExitCode 2 -Message ("Repository path does not contain VisitedTraderTeleport.sln: '{0}'." -f $RepositoryPath)
    }

    if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
        $outputBase = $env:LOCALAPPDATA
        if ([string]::IsNullOrWhiteSpace($outputBase)) {
            $outputBase = $env:TEMP
        }
        if ([string]::IsNullOrWhiteSpace($outputBase)) {
            throw 'Set -OutputRoot or VTT_BUILD_ROOT; neither LOCALAPPDATA nor TEMP is available.'
        }
        $OutputRoot = Join-Path $outputBase 'VisitedTraderTeleport\builds'
    }

    $OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
    $repositoryPrefix = $RepositoryPath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ($OutputRoot.Equals($RepositoryPath, [StringComparison]::OrdinalIgnoreCase) -or
        $OutputRoot.StartsWith($repositoryPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Build -ExitCode 2 -Message 'OutputRoot must be outside RepositoryPath so build records cannot dirty the checkout.'
    }
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
    $logsDirectory = Join-Path $OutputRoot 'logs'
    $artifactDirectory = Join-Path (Join-Path $OutputRoot 'artifacts') $runId
    New-Item -ItemType Directory -Path $logsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $script:LogPath = Join-Path $logsDirectory ($runId + '.log')
    [System.IO.File]::WriteAllText($script:LogPath, '', $script:Utf8NoBom)

    Write-BuildLog -Message ('Build run {0} started.' -f $runId)
    Write-BuildLog -Message ('Requested ref: {0}' -f $Ref)
    Write-BuildLog -Message ('LOG_PATH={0}' -f $script:LogPath)
    Write-BuildLog -Message ('ARTIFACT_DIR={0}' -f $artifactDirectory)

    if ([string]::IsNullOrWhiteSpace($DotNetPath)) {
        $DotNetPath = 'dotnet'
    }
    $GitPath = Resolve-Executable -Command $GitPath -Description 'Git'
    $DotNetPath = Resolve-Executable -Command $DotNetPath -Description '.NET'
    $env:GIT_TERMINAL_PROMPT = '0'

    if ($Fetch) {
        $fetchExitCode = Invoke-BuildCommand -FilePath $GitPath -ArgumentList @(
            '-C', $RepositoryPath, 'fetch', '--no-tags', $RemoteName, $Ref
        ) -Description ('fetch {0} from {1}' -f $Ref, $RemoteName)
        if ($fetchExitCode -ne 0) {
            Stop-Build -ExitCode 3 -Message ("Git fetch failed for '{0}' from '{1}'." -f $Ref, $RemoteName)
        }
        $targetCommit = Get-ResolvedCommit -Revision 'FETCH_HEAD'
    }
    else {
        $targetCommit = Get-ResolvedCommit -Revision $Ref
    }

    $headCommit = Get-ResolvedCommit -Revision 'HEAD'
    Write-BuildLog -Message ('TARGET_COMMIT={0}' -f $targetCommit)

    $statusResult = Invoke-CapturedCommand -FilePath $GitPath -ArgumentList @(
        '-C', $RepositoryPath, 'status', '--porcelain', '--untracked-files=all'
    ) -Description 'check checkout cleanliness'
    if ($statusResult.ExitCode -ne 0) {
        Stop-Build -ExitCode 3 -Message 'Git status failed.'
    }
    if ($statusResult.Lines.Count -ne 0) {
        Stop-Build -ExitCode 2 -Message 'Refusing to build a dirty checkout. Commit, stash, or clean it first.'
    }

    if ($CheckoutRef) {
        if ($headCommit -ne $targetCommit) {
            $checkoutExitCode = Invoke-BuildCommand -FilePath $GitPath -ArgumentList @(
                '-C', $RepositoryPath, 'checkout', '--detach', $targetCommit
            ) -Description ('checkout commit {0}' -f $targetCommit)
            if ($checkoutExitCode -ne 0) {
                Stop-Build -ExitCode 3 -Message ("Git checkout failed for commit '{0}'." -f $targetCommit)
            }

            $postCheckoutStatus = Invoke-CapturedCommand -FilePath $GitPath -ArgumentList @(
                '-C', $RepositoryPath, 'status', '--porcelain', '--untracked-files=all'
            ) -Description 'verify target checkout cleanliness'
            if ($postCheckoutStatus.ExitCode -ne 0) {
                Stop-Build -ExitCode 3 -Message 'Git status failed after checkout.'
            }
            if ($postCheckoutStatus.Lines.Count -ne 0) {
                Stop-Build -ExitCode 2 -Message 'Target checkout is dirty after changing refs; refusing to build it.'
            }
        }
    }
    elseif ($headCommit -ne $targetCommit) {
        Stop-Build -ExitCode 2 -Message (
            "Prepared checkout is at {0}, but ref '{1}' resolves to {2}. Pass -CheckoutRef to allow a detached checkout." -f `
            $headCommit, $Ref, $targetCommit
        )
    }

    $actualCommit = Get-ResolvedCommit -Revision 'HEAD'
    if ($actualCommit -ne $targetCommit) {
        Stop-Build -ExitCode 3 -Message ("Checkout verification failed: expected {0}, found {1}." -f $targetCommit, $actualCommit)
    }

    if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
        if ([string]::IsNullOrWhiteSpace($GamePath)) {
            Stop-Build -ExitCode 2 -Message 'Set -GamePath/VTT_GAME_PATH or provide -ManagedPath and -HarmonyPath.'
        }
        $ManagedPath = Join-Path $GamePath '7DaysToDie_Data\Managed'
    }
    if ([string]::IsNullOrWhiteSpace($HarmonyPath)) {
        if ([string]::IsNullOrWhiteSpace($GamePath)) {
            Stop-Build -ExitCode 2 -Message 'Set -GamePath/VTT_GAME_PATH or provide -ManagedPath and -HarmonyPath.'
        }
        $HarmonyPath = Join-Path $GamePath 'Mods\0_TFP_Harmony\0Harmony.dll'
    }

    if (-not (Test-Path -LiteralPath $ManagedPath -PathType Container)) {
        Stop-Build -ExitCode 2 -Message ("Managed reference directory does not exist: '{0}'." -f $ManagedPath)
    }
    if (-not (Test-Path -LiteralPath $HarmonyPath -PathType Leaf)) {
        Stop-Build -ExitCode 2 -Message ("Harmony reference does not exist: '{0}'." -f $HarmonyPath)
    }
    $ManagedPath = (Resolve-Path -LiteralPath $ManagedPath).Path
    $HarmonyPath = (Resolve-Path -LiteralPath $HarmonyPath).Path

    $managedAssemblies = @(
        'Assembly-CSharp.dll',
        'Assembly-CSharp-firstpass.dll',
        'UnityEngine.CoreModule.dll',
        'UnityEngine.AudioModule.dll',
        'UnityEngine.IMGUIModule.dll',
        'UnityEngine.dll',
        'Newtonsoft.Json.dll'
    )
    $managedDestination = Join-Path $RepositoryPath 'refs\managed'
    $harmonyDestination = Join-Path $RepositoryPath 'refs\harmony'
    New-Item -ItemType Directory -Path $managedDestination -Force | Out-Null
    New-Item -ItemType Directory -Path $harmonyDestination -Force | Out-Null

    try {
        foreach ($assemblyName in $managedAssemblies) {
            $source = Join-Path $ManagedPath $assemblyName
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                Stop-Build -ExitCode 4 -Message ("Required managed reference is missing: '{0}'." -f $source)
            }
            Copy-Item -LiteralPath $source -Destination (Join-Path $managedDestination $assemblyName) -Force
            Write-BuildLog -Message ('Copied managed reference: {0}' -f $assemblyName)
        }
        Copy-Item -LiteralPath $HarmonyPath -Destination (Join-Path $harmonyDestination '0Harmony.dll') -Force
        Write-BuildLog -Message 'Copied Harmony reference: 0Harmony.dll'
    }
    catch {
        Stop-Build -ExitCode 4 -Message ('Reference population failed: {0}' -f $_.Exception.Message)
    }

    Push-Location $RepositoryPath
    try {
        $buildExitCode = Invoke-BuildCommand -FilePath $DotNetPath -ArgumentList @(
            'build', 'src\VisitedTraderTeleport\VisitedTraderTeleport.csproj', '-c', 'Release'
        ) -Description 'Release build and packaging'
        if ($buildExitCode -ne 0) {
            Stop-Build -ExitCode 5 -Message ('Release build failed with exit code {0}.' -f $buildExitCode)
        }

        if ($SkipPackageChecks) {
            Write-BuildLog -Level 'WARN' -Message 'Package verification skipped by explicit request.'
        }
        else {
            $checksExitCode = Invoke-BuildCommand -FilePath $DotNetPath -ArgumentList @(
                'run', '--project', 'devtools\ModChecks', '--', '--package'
            ) -Description 'package verification'
            if ($checksExitCode -ne 0) {
                Stop-Build -ExitCode 6 -Message ('Package verification failed with exit code {0}.' -f $checksExitCode)
            }
        }
    }
    finally {
        Pop-Location
    }

    try {
        [xml]$modInfo = Get-Content -LiteralPath (Join-Path $RepositoryPath 'mod\VisitedTraderTeleport\ModInfo.xml') -Raw
        $version = $modInfo.xml.Version.value
        if ([string]::IsNullOrWhiteSpace($version)) {
            Stop-Build -ExitCode 7 -Message 'ModInfo.xml has no Version value.'
        }
        $packageName = 'VisitedTraderTeleport-{0}.zip' -f $version
        $packagePath = Join-Path (Join-Path $RepositoryPath 'dist') $packageName
        if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
            Stop-Build -ExitCode 7 -Message ("Expected package was not produced: '{0}'." -f $packagePath)
        }
        $artifactPath = Join-Path $artifactDirectory $packageName
        Copy-Item -LiteralPath $packagePath -Destination $artifactPath -Force
        $artifactHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-BuildLog -Message ('ARTIFACT={0}' -f $artifactPath)
        Write-BuildLog -Message ('ARTIFACT_SHA256={0}' -f $artifactHash)
    }
    catch {
        Stop-Build -ExitCode 7 -Message ('Artifact collection failed: {0}' -f $_.Exception.Message)
    }

    Write-BuildLog -Message ('BUILD_SUCCEEDED commit={0} version={1}' -f $targetCommit, $version)
    [Console]::Out.WriteLine('BUILD_RESULT=success')
    [Console]::Out.WriteLine('LOG_PATH={0}' -f $script:LogPath)
    [Console]::Out.WriteLine('ARTIFACT_DIR={0}' -f $artifactDirectory)
    [Console]::Out.WriteLine('ARTIFACT={0}' -f $artifactPath)
    exit 0
}
catch {
    if ($script:LogPath) {
        Write-BuildLog -Level 'ERROR' -Message ('Unexpected failure: {0}' -f $_.Exception.Message)
    }
    [Console]::Error.WriteLine('BUILD_FAILED exit_code=99 message={0}' -f $_.Exception.Message)
    exit 99
}

# Windows builds initiated from Linux

`devtools/Invoke-WindowsBuild.ps1` is the Windows-side entry point for dev and CI-style package builds initiated non-interactively over SSH. It uses the repository's existing Release build and package checker. It does not choose a version, edit the changelog, create a release, or verify behavior in the running game.

The normal flow is:

1. Develop on Linux, then push the commit or ref to the public Git remote.
2. Invoke the script through `ssh omen-build` in `Isolated` mode.
3. Parse the final `VTT_BUILD_RESULT <json>` line and check the SSH exit code.
4. Retrieve the reported log and artifact paths with `scp`.

## Modes

The mode is always explicit; there is no checkout auto-detection.

### Isolated (default)

Every invocation creates a uniquely named repository under `-WorkRoot`, runs `git init`, queries the remote with `git ls-remote`, and performs a depth-1 fetch of only the advertised ref or commit. It checks out that commit in detached-HEAD state and verifies `HEAD` before reading the target project, copying game references, or building.

The script removes only the workspace that it created. It never runs `git clean`, `reset`, `stash`, or another cleanup command against caller-owned files. The workspace is removed after success or failure unless `-KeepWorkspace` is explicitly passed. Keep in mind that a retained workspace contains local game DLL copies under its ignored `refs/` tree.

`-RepositoryPath` is rejected in Isolated mode so it cannot be mistaken for a directory the script might reuse.

### Prepared

Prepared mode is an explicit opt-in for an existing checkout supplied through `-RepositoryPath`. The script:

- acquires a non-blocking per-user file lock keyed by the canonical repository path;
- rejects staged, unstaged, and untracked changes at initial preflight;
- resolves and fetch-verifies the requested target in a separate disposable probe repository;
- requires the Prepared checkout's `HEAD` to equal that remotely verified commit;
- never fetches into, checks out, resets, cleans, or stashes the Prepared checkout;
- rechecks cleanliness and exact `HEAD` immediately before and after the build, and once more after package checks.

Reference copies and normal build outputs are still written to their existing ignored locations. `-OutputRoot` and `-WorkRoot` must both be outside the Prepared checkout. `-KeepWorkspace` is invalid in this mode; the probe is always removed.

## Remote-authoritative refs

Local branches, remote-tracking branches, and tags are never used to resolve `-Ref`. `-RemoteUrl` defaults to `VTT_BUILD_REMOTE_URL`, then the public repository URL. It is passed directly to argument-array Git invocations and is redacted from logs.

Accepted ref forms are:

| Form | Meaning |
| --- | --- |
| `branch:<name>` | Exactly `refs/heads/<name>` on the remote. |
| `tag:<name>` | Exactly `refs/tags/<name>` on the remote, peeled to its commit when annotated. |
| `commit:<40-hex-sha>` | Exactly the supplied full commit SHA. |
| `<40-hex-sha>` | Same as `commit:<sha>`. |
| `<name>` | Queries both the exact branch and exact tag. |

If an unprefixed name exists as both a branch and a tag, the script fails with an explicit instruction to use `branch:` or `tag:`. It never silently prefers one and never falls back to local DWIM resolution.

Each remote attempt pairs `ls-remote` with a bounded fetch. The fetched `FETCH_HEAD^{commit}` must equal the advertised commit. If the ref moves between those operations, the entire pair is resolved again. Only these idempotent network operations are retried, at most three times with short bounded backoff. Checkout verification, build, package checks, and artifact collection are never retried.

Git credential and SSH prompting are disabled with `GIT_TERMINAL_PROMPT=0`, non-interactive credential-manager settings, and SSH `BatchMode=yes`. Unknown SSH host keys fail instead of prompting.

## Game references

`-GamePath` or `VTT_GAME_PATH` supplies the 7 Days to Die installation root. After checking out or verifying the target commit, the script reads the target `VisitedTraderTeleport.csproj` and maps its HintPaths dynamically:

```text
refs\managed\<file>  -> <GamePath>\7DaysToDie_Data\Managed\<file>
refs\harmony\<file>  -> <GamePath>\Mods\0_TFP_Harmony\<file>
```

Every required source DLL must exist before a real build begins. Real runs copy those files into the build repository's ignored `refs/` locations. No game DLL is included in retained artifacts or committed to Git.

## DryRun preflight

`-DryRun` performs the meaningful preflight used before unattended execution:

- validates parameter syntax, paths, Git, and .NET;
- actually queries and resolves the remote ref;
- fetch-verifies the advertised commit in a script-owned disposable repository;
- reads the target project and validates every required game DLL;
- in Prepared mode, validates lock availability, checkout cleanliness, and exact `HEAD` agreement.

It does not mutate the Prepared checkout, copy references, run the build, run `ModChecks`, or create an artifact. The temporary Isolated/probe repository needed to inspect the target project is removed unless an Isolated caller explicitly requests `-KeepWorkspace`.

## Timeouts

`-TimeoutSeconds` sets the overall wall-clock deadline and defaults to 1800 seconds. Child processes are also bounded by phase:

| Phase | Budget |
| --- | ---: |
| Each `ls-remote` + fetch attempt | 120 seconds |
| Disposable workspace preparation | 300 seconds |
| Release build | 900 seconds |
| `ModChecks --package` | 600 seconds |

Every phase is capped by the remaining overall deadline. On timeout the script terminates the full child process tree (`taskkill /T /F` on Windows), captures available stdout/stderr, removes owned workspaces when applicable, and returns exit code 10 with `timedOut: true`.

## SSH invocation

An Isolated build using machine-local environment variables for game/output paths can be invoked from Linux with:

```bash
ssh omen-build 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "C:\src\VisitedTraderTeleport\devtools\Invoke-WindowsBuild.ps1" -Mode Isolated -Ref "branch:feature/40-windows-ssh-build"'
```

With explicit machine paths:

```bash
ssh omen-build 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "C:\src\VisitedTraderTeleport\devtools\Invoke-WindowsBuild.ps1" -Mode Isolated -Ref "commit:0123456789abcdef0123456789abcdef01234567" -GamePath "D:\path\to\7 Days To Die" -OutputRoot "C:\builds\VisitedTraderTeleport" -WorkRoot "C:\builds\VisitedTraderTeleport\workspaces"'
```

Prepared preflight is intentionally more explicit:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
  -File .\devtools\Invoke-WindowsBuild.ps1 `
  -Mode Prepared `
  -RepositoryPath C:\src\VisitedTraderTeleport `
  -Ref commit:0123456789abcdef0123456789abcdef01234567 `
  -DryRun
```

The normal build path runs and requires both existing commands to succeed:

```powershell
dotnet build src\VisitedTraderTeleport\VisitedTraderTeleport.csproj -c Release
dotnet run --project devtools\ModChecks -- --package
```

`-SkipPackageChecks` remains an explicit diagnostic escape hatch and is logged as a warning. Builds intended for handoff should not use it.

## Logs, artifacts, and result JSON

The output root comes from `-OutputRoot`, then `VTT_BUILD_ROOT`, then the current Windows user's local application-data directory. Normal runs create:

```text
<output-root>\logs\<UTC-run-id>.log
<output-root>\artifacts\<UTC-run-id>\VisitedTraderTeleport-<version>.zip
```

Logs are structured UTF-8 text with UTC timestamps, phase commands, exit codes, the exact resolved commit, and the artifact SHA-256. The ZIP name must exactly match the version read from `ModInfo.xml`; artifact collection never uses a `dist\*.zip` glob.

The final stdout line is always the single machine-readable record:

```text
VTT_BUILD_RESULT {"result":"success","exitCode":0,"phase":"complete","mode":"Isolated","resolvedCommit":"...","attempt":1,"timedOut":false,"artifact":{"path":"...","sha256":"..."}}
```

The complete record also includes the message, DryRun flag, requested ref, run ID, log path, workspace path, and whether the workspace was kept. Parse this line instead of scraping human-readable progress output.

After substituting paths from the result, retrieve files from Linux with commands such as:

```bash
scp 'omen-build:C:/builds/VisitedTraderTeleport/logs/<UTC-run-id>.log' ./
scp 'omen-build:C:/builds/VisitedTraderTeleport/artifacts/<UTC-run-id>/VisitedTraderTeleport-<version>.zip' ./
```

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Build/preflight succeeded. |
| 2 | Parameter, path, or executable validation failed. |
| 3 | Remote resolution, network retry, ambiguity, or unstable-ref verification failed. |
| 4 | Disposable workspace preparation or cleanup failed. |
| 5 | Required game references could not be validated or copied. |
| 6 | Prepared lock, cleanliness, or exact-HEAD verification failed. |
| 7 | Release build failed. |
| 8 | `ModChecks --package` failed. |
| 9 | Exact artifact identification, copy, or hashing failed. |
| 10 | An overall or phase timeout expired. |
| 99 | An unexpected script failure occurred. |

Compilation success means only that the project compiled and its package passed repository checks against the supplied game DLLs. In-game verification is a separate activity and must be reported separately.

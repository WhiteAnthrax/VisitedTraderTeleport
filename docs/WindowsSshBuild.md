# Windows builds initiated from Linux

`devtools/Invoke-WindowsBuild.ps1` is the Windows-side entry point for dev and CI-style package builds initiated non-interactively over SSH. It uses the repository's existing build and package checker; it does not replace the human-gated release procedure, choose a version, edit the changelog, create a release, or verify behavior in the running game.

The intended flow is:

1. Develop on Linux, then push the commit or branch to `origin`.
2. Invoke the script in a persistent Windows clone through `ssh omen-build`.
3. Read the final `LOG_PATH` and `ARTIFACT` lines from standard output.
4. Retrieve the timestamped log and packaged ZIP with `scp` or another SSH file-transfer tool.

## Contract

`-Ref` is required and may name a branch, tag, or commit. The script resolves it to a full commit hash and verifies that the build runs at exactly that commit.

The default is prepared-checkout mode: the Windows clone must already be at the requested commit, and the script does not change Git state. `-Fetch` explicitly allows a fetch of the requested ref from `-RemoteName` (default `origin`), and `-CheckoutRef` explicitly allows a clean checkout to move to the resolved commit in detached-HEAD state. Every mode rejects tracked or untracked changes so the package always corresponds to the reported commit; ignored build outputs and `refs/` inputs do not make the checkout dirty.

Game DLLs are local-only inputs and remain under the ignored `refs/` tree:

- `-GamePath` (or `VTT_GAME_PATH`) supplies the game root. The script derives the Managed and Harmony locations beneath it.
- `-ManagedPath` overrides the Managed directory.
- `-HarmonyPath` overrides the path to `0Harmony.dll`.

Only the seven managed DLLs referenced by `VisitedTraderTeleport.csproj` and `0Harmony.dll` are copied. No game DLL is placed in an artifact or committed to Git.

The output root comes from `-OutputRoot`, then `VTT_BUILD_ROOT`, then the current Windows user's local application-data directory. It must be outside the repository so logs and retained artifacts cannot dirty the checkout. Each invocation creates:

```text
<output-root>\logs\<UTC-run-id>.log
<output-root>\artifacts\<UTC-run-id>\VisitedTraderTeleport-<version>.zip
```

Logs use UTF-8, UTC timestamps, command exit codes, the exact target commit, and the artifact SHA-256. The script also emits `BUILD_RESULT`, `LOG_PATH`, `ARTIFACT_DIR`, and `ARTIFACT` lines for the SSH caller. Do not parse the human-readable progress lines when those keys are sufficient.

## SSH invocation

After the script is available in the persistent Windows clone, a Linux caller can run:

```bash
ssh omen-build 'powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "C:\src\VisitedTraderTeleport\devtools\Invoke-WindowsBuild.ps1" -RepositoryPath "C:\src\VisitedTraderTeleport" -Ref "feature/40-windows-ssh-build" -Fetch -CheckoutRef -GamePath "D:\path\to\7 Days To Die" -OutputRoot "C:\builds\VisitedTraderTeleport"'
```

For a machine-local setup, prefer setting `VTT_GAME_PATH` and `VTT_BUILD_ROOT` in the SSH user's environment so the invocation contains no installation-specific paths. `VTT_DOTNET_PATH` may point to `dotnet.exe` when it is not on `PATH`. `-GitPath` and `-DotNetPath` also accept explicit executable paths.

To build a checkout prepared by another tool, omit `-Fetch` and `-CheckoutRef`:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
  -File .\devtools\Invoke-WindowsBuild.ps1 `
  -RepositoryPath C:\src\VisitedTraderTeleport `
  -Ref 0123456789abcdef0123456789abcdef01234567
```

The normal path runs both commands and requires both to succeed:

```powershell
dotnet build src\VisitedTraderTeleport\VisitedTraderTeleport.csproj -c Release
dotnet run --project devtools\ModChecks -- --package
```

`-SkipPackageChecks` exists for targeted diagnosis and is logged as a warning. Builds intended for handoff should not use it.

Use the paths printed by the successful invocation to retrieve results. For example, after substituting the reported run ID:

```bash
scp 'omen-build:C:/builds/VisitedTraderTeleport/logs/<UTC-run-id>.log' ./
scp 'omen-build:C:/builds/VisitedTraderTeleport/artifacts/<UTC-run-id>/VisitedTraderTeleport-<version>.zip' ./
```

## Exit codes

| Code | Meaning |
| ---: | --- |
| 0 | Build, verification, and artifact collection succeeded. |
| 2 | A parameter, local path, executable, or checkout precondition is invalid. |
| 3 | Fetch, ref resolution, checkout, or checkout verification failed. |
| 4 | Required game references could not be validated or copied. |
| 5 | The existing Release build failed. |
| 6 | `ModChecks --package` failed. |
| 7 | The expected package could not be identified, hashed, or copied. |
| 99 | An unexpected script failure occurred. |

A nonzero result writes `BUILD_FAILED exit_code=<code> message=<reason>` to standard error and records the failure in the run log when the output directory was successfully initialized.

Compilation success means only that the project compiled and its package passed repository checks against the supplied game DLLs. In-game verification is a separate activity and must be reported separately.

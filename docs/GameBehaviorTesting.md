# Game Behavior Testing (vtttest console command)

This mod ships a hidden, Debug-only console command (`vtttest`) that calls the same
production methods a real trader dialog interaction would — `VisitedTraderStore.Record`
and `DialogActionVisitedTraderTeleport.PerformAction` — so a script can drive and verify
the mod's actual teleport behavior without any screen automation. It exercises the real
client→server request and the real server-side execution; it does not simulate the trader
dialog UI itself (opening the dialog, clicking a response).

This complements the Tier1 unit tests (`tests/VisitedTraderTeleport.Tests`, engine-free
logic only, run in GitHub Actions) with a second tier that runs inside an actual game
process and can be pointed at a real dedicated server.

## Building a test-enabled DLL

`vtttest` only exists in `Debug` builds (`VTT_TEST_HARNESS` is defined only when
`Configuration=Debug`; see `VisitedTraderTeleport.csproj`). A normal `Release` build
(what `Invoke-WindowsBuild.ps1` and `ModChecks --package` always produce) never contains
this code, so it can't leak into a distributed package.

```
dotnet build src/VisitedTraderTeleport/VisitedTraderTeleport.csproj -c Debug
```

## Enabling it at runtime

Even in a Debug build, `vtttest` refuses to do anything unless a marker file sits next to
the mod DLL:

```
Mods/VisitedTraderTeleport/EnableTestHarness.txt   (empty file, contents don't matter)
```

This is a second, independent guard against a Debug build accidentally doing something on
a server nobody intended to test against.

## Commands

Run these from the in-game F1 console, a Telnet session, or RCON:

| Command | Effect |
|---|---|
| `vtttest record <traderEntityId>` | Records a visit for the resolved player, exactly as if they'd opened that trader's dialog. Find an entity id with the vanilla `le` (list entities) command. |
| `vtttest teleport <destinationKey>` | Runs the real "Travel" action for that destination key, including the real network request when run against a remote client. |
| `vtttest list` | Prints the resolved player's currently visible destinations (key + display name). |

Each command also emits a single-line result marker to the console/log:

```
VTT_TEST_RESULT {"action":"teleport","ok":true,"detail":"npcTraderBob:786:-2336"}
```

An external driver can grep for this line instead of parsing free-form output.

## Verifying against a Docker dedicated server

This repo does not ship or maintain a Docker Compose setup for the dedicated server —
use whatever server environment you already have. The requirements to drive `vtttest`
remotely are just:

- **Telnet enabled** in the server's config (`ServerTelnetEnabled`/`TelnetPort`, or your
  existing RCON path if you have one — anything that can send raw console command text
  will work, since `vtttest` is just another console command).
- **A Debug build of `VisitedTraderTeleport.dll`**, plus `EnableTestHarness.txt`, present
  in the container's `Mods/VisitedTraderTeleport/` folder.
- **Read access to the save directory** from outside the container (a mounted volume, or
  `docker cp`/`scp`), so a driver script can read `VisitedTraderTeleportData.json`
  directly as ground truth after issuing `vtttest` commands — this file already exists
  today (written by `VisitedTraderStore.SaveDatabase`) and needs no changes to use this
  way.

A typical driver script sends ordinary console commands (e.g. `giveself`, `tp`) mixed with
`vtttest` commands over the same Telnet connection, waits a beat for the destination
preparation window (see `VisitedTraderTeleportService`'s `timeout=Ns` log line), and then
diffs `VisitedTraderTeleportData.json` before/after to confirm the expected visit or
teleport actually landed.

## Known limitation

`vtttest` bypasses the trader dialog UI entirely (by design — it calls the same methods
the UI would call, skipping the UI itself). It cannot tell you whether the dialog renders
correctly, whether a player can actually click through to a destination, or anything else
about `DialogPatches.cs`'s XUi integration. Those still need a manual playtest.

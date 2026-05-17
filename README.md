# Travel Between Visited Traders

7 Days to Die v2.6 mod that adds a trader dialog option to travel to traders the player has already visited.

## What This Mod Does

- Talking to a trader records that trader as visited for the current save.
- The trader dialog gains a `Travel to a visited trader` option.
- Selecting it shows dynamically generated destinations with trader name, distance, direction, and coordinates.
- Choosing a destination teleports the player to that trader's recorded position.
- If the destination area is not loaded yet, the mod briefly prepares travel before teleporting.
- In multiplayer, the client also warms and refreshes destination visuals around teleport to reduce transparent POI objects after travel.
- Newly recorded visit data is saved in the current save folder as `VisitedTraderTeleportData.json`.
- Older `VisitedTraderTeleportVisited.txt` save data remains readable for compatibility.

## Access Modes

Access modes are mainly for multiplayer. Single-player users can keep the default `personal` mode and use the mod much like older versions.

- `personal`
  Only traders visited by that player are available. This is the default mode.
- `party`
  Traders visited by that player or their current party members are available.
- `shared`
  Traders visited by anyone in the active save or server data are available to everyone.

In multiplayer, the server-side setting is authoritative. Clients use the destination list allowed by the server.

## Which Mode Should I Use

- Use `personal` for individual unlocks or normal single-player use.
- Use `party` for co-op groups that should share discoveries only while grouped.
- Use `shared` for server-wide unlocks.

## How To Change The Mode

After installation, edit:

```text
7 Days To Die\Mods\VisitedTraderTeleport\Config\VisitedTraderTeleport.xml
```

Change this value:

```xml
<AccessMode value="personal" />
```

to one of:

```xml
<AccessMode value="personal" />
<AccessMode value="party" />
<AccessMode value="shared" />
```

For multiplayer, change the config on the server. Client-side config does not decide multiplayer behavior.

Restart the affected game process after changing the file:

- Single-player: restart the game.
- Multiplayer server: restart the dedicated server or host process.

## Multiplayer Behavior

For multiplayer saves, the server owns trader access:

- When a client talks to a trader, the client reports that visit to the server.
- The server records the visit, applies `personal`, `party`, or `shared`, and returns the allowed destination list.
- When a client chooses a teleport destination, the server validates that destination again before performing the teleport.
- `party` mode checks the player's current party when the destination list is requested.

## Existing Users Upgrading From Version 0.2.x Or Older

Version `0.2.x` and older saved visited traders in:

```text
VisitedTraderTeleportVisited.txt
```

Version `0.3.0` and newer save new visit ownership in:

```text
VisitedTraderTeleportData.json
```

Upgrade behavior:

- Existing `VisitedTraderTeleportVisited.txt` entries from `0.2.x` or older continue to load after upgrading.
- Those legacy entries are treated as a compatibility-wide shared pool, so already available destinations do not suddenly disappear.
- Legacy entries are not auto-rewritten into the new ownership-aware JSON schema.
- New trader visits after upgrading are written to `VisitedTraderTeleportData.json`.
- The new JSON format keeps trader destination data separate from player ownership, so changing between `personal`, `party`, and `shared` later does not require resetting new-version data.

For dedicated-server migration, legacy data is read from the machine that owns the active save. Old TXT files that exist only on former clients are not transferred automatically.

## New Users Starting Fresh

If this is your first install:

- There is no legacy migration step.
- The default mode is `personal`.
- New trader visits are recorded in `VisitedTraderTeleportData.json`.
- You can switch to `party` or `shared` later without resetting new-version data.

## Build

Reference DLLs and vanilla config copies are expected under `refs/`.

```powershell
dotnet build src\VisitedTraderTeleport\VisitedTraderTeleport.csproj -c Release
```

The build copies `VisitedTraderTeleport.dll` into `mod\VisitedTraderTeleport`.
It also creates a versioned package under `dist\`, for example `VisitedTraderTeleport-0.4.2.zip`.
The package contains a top-level `VisitedTraderTeleport` folder ready to extract into the game's `Mods` directory.

Update `CHANGELOG.md` whenever behavior, packaging, or project-facing workflow changes. The build copies it into the packaged mod as `Changelog.txt`.

## Install

For normal users, download the versioned ZIP from GitHub Releases and extract the `VisitedTraderTeleport` folder to the game's `Mods` directory:

```text
7 Days To Die\Mods\VisitedTraderTeleport
```

The mod requires the vanilla `0_TFP_Harmony` mod that ships with current 7 Days to Die versions.

For multiplayer, install this mod on both the server and every client. The server owns multiplayer visit records and access decisions, while each client needs the dialog/UI patch and the network request handling.

Anti-cheat should be disabled for this DLL mod.

## License

This project is licensed under the MIT License. It does not include or license any 7 Days to Die game assets. 7 Days to Die is property of The Fun Pimps.

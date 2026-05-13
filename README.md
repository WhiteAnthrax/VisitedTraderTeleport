# Travel Between Visited Traders

7 Days to Die v2.6 mod that adds a trader dialog option to travel to traders the player has already visited.

## What This Mod Does

- Talking to a trader records that trader as visited for the current save.
- The trader dialog gains a `Travel to a visited trader` option.
- Selecting it shows dynamically generated destinations for recorded traders.
- Choosing a destination teleports the player to that trader's recorded position.
- Newly recorded visit data is saved in the current save folder as `VisitedTraderTeleportData.json`.
- Older `VisitedTraderTeleportVisited.txt` save data remains readable for compatibility.

## Access Modes

The mod has three access modes. Pick the one that matches how you want trader discovery to work.

- `personal`
  Only traders that this player has visited are available.
  This is the default and is closest to a personal fast-travel unlock list.
- `party`
  Traders visited by this player or by their current party members are available.
  This is useful for co-op groups that want discovery to be shared while they are actively grouped.
- `shared`
  Any trader visited by anyone in the active save or server data becomes available to everyone.
  This is the most permissive option and works like a world-wide trader unlock list.

In multiplayer, the server-side setting is authoritative. Clients receive the destination list allowed by the server and cannot override the server's mode locally.

## Which Mode Should I Use

- Use `personal` if each player should unlock traders for themselves.
- Use `party` if your group explores together and you want discoveries to be shared only while players are in the same party.
- Use `shared` if your server treats trader discovery as global progression for everyone.

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

For multiplayer saves, trader ownership and destination access are evaluated by the server:

- When a client talks to a trader, the client reports that visit to the server.
- The server records the visit, applies `personal`, `party`, or `shared`, and sends back the destination list currently allowed for that player.
- When a client chooses a teleport destination, the server validates that destination again before performing the teleport.
- `party` mode uses the player's current party at the time the destination list is requested, so players can join or leave parties without corrupting saved ownership data.

## Existing Users Upgrading From Older Versions

Older releases saved visited traders in:

```text
VisitedTraderTeleportVisited.txt
```

Current releases save new visit ownership in:

```text
VisitedTraderTeleportData.json
```

Upgrade behavior is designed to avoid surprising data loss:

- Existing `VisitedTraderTeleportVisited.txt` entries continue to load after upgrading.
- Those legacy entries are treated as a compatibility-wide shared pool, so destinations that were already available do not suddenly disappear.
- Legacy entries are not auto-rewritten into the new ownership-aware JSON schema.
- New trader visits are written to `VisitedTraderTeleportData.json`.
- The new JSON format keeps trader destination data separate from player ownership, so changing between `personal`, `party`, and `shared` later does not require resetting new-version data.

For dedicated-server migration, legacy data is read from the machine that owns the active save. Old TXT files sitting only on former clients are not automatically transferred to a new server.

## New Users Starting Fresh

If this is your first install:

- There is no legacy migration step.
- The default mode is `personal`.
- New trader visits are recorded in `VisitedTraderTeleportData.json`.
- You can switch to `party` or `shared` later without resetting that new-version data.

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

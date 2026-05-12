# Travel Between Visited Traders

7 Days to Die v2.6 mod that adds a trader dialog option to travel to traders the player has already visited.

## Behavior

- Talking to a trader records that trader as visited for the current save.
- The trader dialog gains a `Travel to a visited trader` option.
- Selecting it shows dynamically generated destinations for recorded traders.
- Choosing a destination teleports the player to that trader's recorded position.
- Newly recorded visit data is saved in the current save folder as `VisitedTraderTeleportData.json`.
- Older `VisitedTraderTeleportVisited.txt` save data remains readable for compatibility.

## Access Modes And Save Compatibility

`Mods\VisitedTraderTeleport\Config\VisitedTraderTeleport.xml` contains the access mode setting after installation:

- `personal`: only traders newly visited by that player are available.
- `party`: traders newly visited by that player or by current party members are available.
- `shared`: all newly recorded trader visits in the active data set are available.

The default is `personal`.

In multiplayer, the server-side config is authoritative. Clients receive the currently allowed destination list from the server and follow the server's access mode.

Existing users can upgrade without deleting their old save-side data:

- Legacy `VisitedTraderTeleportVisited.txt` entries continue to load.
- Legacy entries are treated as a compatibility-wide shared pool so previously available destinations do not disappear during migration.
- Legacy entries are not auto-rewritten into the new ownership-aware JSON schema.
- New visits are written only to `VisitedTraderTeleportData.json`, which keeps trader destination data and per-player visit ownership separately.
- Because new data keeps ownership information, changing the access mode later does not require throwing away the new save data.

For multiplayer saves, visit ownership and destination access are evaluated by the server:

- When a client talks to a trader, the client reports that visit to the server.
- The server records the visit, applies `personal`, `party`, or `shared`, and sends back the currently allowed destination list for that player.
- When a client chooses a teleport destination, the server validates that destination again before performing the teleport.
- `party` mode is evaluated from the player's current party at the time the destination list is requested, so party membership can change without corrupting saved ownership data.

Legacy compatibility data applies on whichever machine owns the active save. For dedicated-server migration, old client-local TXT files are not transferred automatically to the server.

## Build

Reference DLLs and vanilla config copies are expected under `refs/`.

```powershell
dotnet build src\VisitedTraderTeleport\VisitedTraderTeleport.csproj -c Release
```

The build copies `VisitedTraderTeleport.dll` into `mod\VisitedTraderTeleport`.
It also creates a versioned package under `dist\`, for example `VisitedTraderTeleport-0.2.0.zip`.
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

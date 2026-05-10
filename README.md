# Travel Between Visited Traders

7 Days to Die v2.6 mod that adds a trader dialog option to travel to traders the player has already visited.

## Behavior

- Talking to a trader records that trader as visited for the current save.
- The trader dialog gains a `Travel to a visited trader` option.
- Selecting it shows dynamically generated destinations for recorded traders.
- Choosing a destination teleports the player to that trader's recorded position.
- Visited trader data is saved in the current save folder as `VisitedTraderTeleportVisited.txt`.

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

For multiplayer, install this mod on both the server and every client. This is common for DLL/Harmony mods that change client-visible UI or dialog behavior; server-side-only installation is generally limited to simpler server-only mods.

Anti-cheat should be disabled for this DLL mod.

## License

This project is licensed under the MIT License. It does not include or license any 7 Days to Die game assets. 7 Days to Die is property of The Fun Pimps.

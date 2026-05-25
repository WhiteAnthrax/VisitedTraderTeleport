# VisitedTraderTeleport Test Tools

`VisitedTraderTeleportTestTools` is a local-only helper mod for verifying trader travel behavior.
It is not part of the public player package.

## What It Does

- When a local player enters a world, it can preload the nearest known trader area.
- If a trader NPC is found, it teleports the player two blocks in front of that trader.
- It can seed all resolved traders as visited for the local player without requiring the main mod to carry test-only controls.

## How To Use

Install both folders locally:

```text
7 Days To Die\Mods\VisitedTraderTeleport
7 Days To Die\Mods\VisitedTraderTeleportTestTools
```

Start a local test world. By default, the test tools mod moves the local player toward a trader area and seeds all resolved traders as visited for that player.

The test tools settings live in:

```text
7 Days To Die\Mods\VisitedTraderTeleportTestTools\Config\VisitedTraderTeleportTestTools.xml
```

Use `RecordAllTradersOnGameStart` to enable or disable visit seeding.

## Safety

Keep this helper out of normal play and public release packages. It exists only to speed up local verification.

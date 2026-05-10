# Travel Between Visited Traders Changelog

## 0.2.1 - 2026-05-11 03:36 JST
- Changed the public display title to Travel Between Visited Traders.
- Updated README wording to match the Nexus Mods title.

## 0.2.0 - 2026-05-11 01:50 JST
- Renamed the internal project, namespace, assembly, DLL, and solution to VisitedTraderTeleport.
- Renamed current XML/localization IDs from tt_* to vtt_*.
- Renamed the visited trader data file to VisitedTraderTeleportVisited.txt.

## 0.1.12 - 2026-05-11 01:45 JST
- Renamed the public mod display name to Visited Trader Teleport.
- Changed generated release packages to use the VisitedTraderTeleport folder and ZIP name.
- Updated README wording to describe teleporting between previously visited traders more clearly.

## 0.1.11 - 2026-05-11 01:40 JST
- Adjusted teleport target height so saved player positions are not lowered by terrain correction.
- Added a small upward clearance to reduce floor clipping after teleporting.

## 0.1.10 - 2026-05-10 02:57 JST
- Removed developer-facing repository notes from the public README.

## 0.1.9 - 2026-05-10 02:46 JST
- Added README multiplayer install guidance for server and client installation.

## 0.1.8 - 2026-05-10 01:30 JST
- Changed newly recorded trader destinations to use the talking player's position.
- Kept compatibility for older saved destinations that used trader position plus forward offset.

## 0.1.7 - 2026-05-10 01:14 JST
- Added root CHANGELOG.md as the source changelog for GitHub.
- Changed the build to copy CHANGELOG.md into the packaged mod as Changelog.txt.
- Treat mod/TraderTeleport/Changelog.txt as a generated package file.

## 0.1.6 - 2026-05-10 00:53 JST
- Changed release packages to include the top-level TraderTeleport folder.
- Added an MIT License for the project.
- Added README notes for package layout and game asset ownership.

## 0.1.5 - 2026-05-09 04:40 JST
- Removed the extra no-destination dialog response.
- When there are no teleport destinations, only the vanilla "Never mind" option is shown.
- Removed the unused tt_no_visited localization entry.

## 0.1.4 - 2026-05-09 04:35 JST
- Added date/time information to changelog entries.
- Added a project rule to include timestamps in future changelog entries.

## 0.1.3 - 2026-05-09 04:33 JST
- Added this changelog file to the packaged mod.
- Added a project rule to keep this changelog updated for future changes.

## 0.1.2 - 2026-05-09 04:31 JST
- Added automatic versioned ZIP packaging after successful builds.
- Versioned packages are created under dist as TraderTeleport-<version>.zip.

## 0.1.1 - 2026-05-09 04:29 JST
- Changed the mod author to WhiteAnthrax.
- Added a rule to bump the mod version for every functional or packaged change.

## 0.1.0 - 2026-05-09 04:25 JST
- Added a trader dialog option to travel to previously visited traders.
- Records visited traders per save.
- Dynamically lists visited trader destinations in the trader dialog.
- Teleports to the selected trader destination.
- Supports English and Japanese text for the added dialog and tooltip.
- Teleports to two blocks in front of the destination trader.
- Excludes the current trader from the destination list.

# Travel Between Visited Traders Changelog

## 0.4.10 - 2026-05-17 16:17 JST
- Added a preventative stability improvement for reported freezes when teleporting to distant or unloaded trader destinations. The reported freeze was not reproducible in local testing.
- Destination areas are now prepared briefly before teleporting when needed.
- Avoided synchronous terrain-height lookup on unloaded chunks to reduce the chance of short freezes during travel.
- Fixed current trader filtering by keeping the active trader for the dialog session and matching by trader area as well as key.

## 0.4.7 - 2026-05-17 03:54 JST
- Added an opt-in local testing mode that records all known traders when talking to any trader.
- Test-mode destinations are saved two blocks in front of each trader's facing direction to make unloaded-destination testing easier.

## 0.4.6 - 2026-05-17 03:36 JST
- Added a short "Preparing travel..." step that asks the game to load the destination area before teleporting when needed.
- Reduced possible short freezes when teleporting to distant traders by avoiding synchronous terrain-height lookup for unloaded destinations.

## 0.4.5 - 2026-05-13 20:52 JST
- Repackaged the release with a new version number for Nexus Mods upload retry.
- No functional changes from 0.4.4.

## 0.4.4 - 2026-05-13 12:37 JST
- Clarified that access modes mainly target multiplayer while single-player users can keep the default personal mode.
- Tightened README, Nexus Mods copy, and config comments to reduce repetition and improve first-read clarity.

## 0.4.3 - 2026-05-13 12:22 JST
- Added a maintained Nexus Mods BBCode description file for future release updates.
- Updated project rules so Nexus Mods public copy is refreshed alongside player-facing documentation changes.
- Corrected versioning/package rule references to the current VisitedTraderTeleport project paths.

## 0.4.2 - 2026-05-13 09:36 JST
- Reworked the README for end users with clearer access mode guidance, setup steps, upgrade behavior, and new-install notes.
- Clarified how `personal`, `party`, and `shared` differ in single-player and multiplayer usage.

## 0.4.1 - 2026-05-13 08:45 JST
- Removed invalid fixed NetPackage ID registration that caused dedicated servers to log out-of-range registration errors during startup.
- Switched the custom multiplayer packages back to 7 Days to Die's built-in NetPackage type discovery and runtime mapping flow.

## 0.4.0 - 2026-05-13 01:50 JST
- Added server-authoritative multiplayer synchronization for trader visit reports, destination list refreshes, and teleport requests.
- Made the server-side access mode config authoritative in multiplayer while clients follow the server-provided destination list.
- Kept `personal`, `party`, and `shared` behavior compatible with the ownership-aware JSON save schema.
- Updated the README to explain multiplayer behavior, current-party evaluation, and dedicated-server migration expectations.

## 0.3.0 - 2026-05-13 01:29 JST
- Added access-mode groundwork for personal, party, and shared visited-trader visibility.
- Added a new JSON save schema that keeps trader destinations separate from per-player visit ownership.
- Preserved compatibility with legacy `VisitedTraderTeleportVisited.txt` data by loading it as a shared compatibility pool.
- Added a packaged XML config file for selecting the access mode.
- Documented upgrade behavior and the current multiplayer synchronization limitation.

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

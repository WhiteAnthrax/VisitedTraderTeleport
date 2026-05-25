# Travel Between Visited Traders Changelog

## 0.5.4 - 2026-05-26 02:03 JST
- Moved destination screen status lines out of the selectable response list so only real actions show as selectable dialog choices.
- No save reset is required.

## 0.5.3 - 2026-05-26 01:38 JST
- Added `perMeter` travel cost configuration for finer distance-based cost tuning while keeping older `perKilometer` configs compatible.
- Destination choices now show the configured travel cost before selecting a trip.
- Added sound-key diagnostics for the optional travel transition sound and tried both bracketed and unbracketed sound keys when matching loaded audio data.
- No save reset is required.

## 0.5.2 - 2026-05-26 01:22 JST
- Fixed config loading for the travel cost and transition settings by using the active mod folder path provided by the game.
- Added clearer startup logging for the loaded travel cost and transition settings.
- Tried both player-local and position-based playback for the optional travel sound.
- No save reset is required.

## 0.5.1 - 2026-05-26 00:31 JST
- Fixed the trader transport setup so travel costs, multiplayer transition messages, and the optional travel sound can run correctly.
- Removed leftover local-only verification code from the main mod package.
- No save reset is required.

## 0.5.0 - 2026-05-25 05:18 JST
- Added a short trader-transport transition before travel, with a brief hidden-view delay, optional vehicle sound, and arrival message.
- Added optional distance-based travel costs using configurable items, with gas as the default cost item when enabled.
- Multiplayer servers now send cost settings to clients so destination labels can show the server-side travel cost.
- No save reset is required.

## 0.4.26 - 2026-05-25 11:14 JST
- Added paging controls to the visited-trader destination screen so long lists remain fully reachable.
- No save reset is required.

## 0.4.25 - 2026-05-23 23:48 JST
- Fixed config loading when the installed mod folder is named something other than `VisitedTraderTeleport`, such as `Travel Between Visited Traders`.
- Access mode settings now load from the actual installed mod folder beside `VisitedTraderTeleport.dll`, with the old path kept as a fallback.
- No save reset is required.

## 0.4.24 - 2026-05-23 19:45 JST
- Improved same-trader matching for old TXT/JSON records whose raw/internal trader IDs differ, such as `npcTraderRekt` and `traderrekt`.
- Reused existing matching trader records when recording a visit, preventing the same trader from being saved again under a slightly different key during the same session.
- No save reset is required.

## 0.4.23 - 2026-05-23 08:24 JST
- Clarified upgrade and save-data compatibility documentation for existing users.
- Added user-facing notes that existing JSON/TXT data is kept, old TXT data still loads, and automatic cleanup does not require a save reset.
- Added a server upgrade safety tip recommending a manual copy of visited trader data files before updating.

## 0.4.22 - 2026-05-23 07:03 JST
- Added friendly trader name mappings for Wasteland Mod traders Gene, Johnny, and Rad Cat.
- Recognized likely raw/internal Wasteland name tokens such as `tradergene`, `traderjohnny`, and `spheretest`.
- Unknown modded trader names still use the existing generic fallback.
- No save reset is required.

## 0.4.21 - 2026-05-23 06:48 JST
- De-duplicated same-name traders recorded in the same trader area, even when their local position buckets differ.
- Kept distinct same-area traders separate when their display names differ, improving compatibility with overhaul mods that place multiple traders in one area.
- Existing same-name duplicate records are merged automatically on load.
- No save reset is required.

## 0.4.20 - 2026-05-23 01:17 JST
- Avoided forcing a teleport if the destination area is still not ready after preparation.
- Added clearer server logs for destination preparation, ready, and abort cases to help diagnose unloaded or not-yet-generated trader areas.
- Added a localized "destination is not ready yet" message for local/single-player abort cases.
- Increased destination preparation timeout from 4 seconds to 8 seconds.
- No save reset is required.

## 0.4.19 - 2026-05-22 20:31 JST
- Fixed duplicate destination entries when the same trader was recorded from slightly different interaction positions.
- Separated trader identity from teleport destination position: the trader's position is used for stable matching, while the player's conversation position remains the saved travel destination.
- Existing nearby duplicate trader records are merged automatically on load when they refer to the same trader.
- No save reset is required.

## 0.4.18 - 2026-05-22 19:49 JST
- Normalized legacy TXT destinations in memory so old shared data is less likely to duplicate JSON destinations.
- De-duplicated destination lists when legacy TXT and JSON records refer to the same nearby trader.
- Refined same-trader matching so distinct detailed keys inside the same trader area are not merged just because they share an area.
- No save reset is required.

## 0.4.17 - 2026-05-22 15:39 JST
- Adjusted canonical trader keys to include local trader position within the trader area.
- Reduced the chance that mods with multiple traders in the same trader area merge distinct traders into one destination.
- No save reset is required.

## 0.4.16 - 2026-05-22 14:39 JST
- Stabilized trader visit keys by normalizing server-side visit data to trader area coordinates when possible.
- Existing duplicate trader entries caused by older position-based keys are merged into canonical trader-area keys on load.
- A one-time backup named `VisitedTraderTeleportData.before-0.4.16.json` is created before automatic JSON normalization.
- Improved current-trader filtering when old position-based keys and new trader-area keys refer to the same nearby trader.
- No save reset is required.

## 0.4.15 - 2026-05-22 14:08 JST
- Fixed a startup error introduced in 0.4.14 by removing the unsupported dialog header Harmony patch.
- Changed the current access mode and no-destinations notices to use safe dynamic dialog rows.
- No save reset is required.

## 0.4.14 - 2026-05-22 13:58 JST
- Fixed the destination screen status text so the current access mode and no-destinations message are returned through the dialog header text used by the game UI.
- No save reset is required.

## 0.4.13 - 2026-05-22 12:51 JST
- Added the active access mode to the trader travel destination screen.
- Added a clear no-destinations message when the current mode has no valid destinations after excluding the current trader.
- Fixed the client-side snapshot refresh request when opening trader dialog in multiplayer.
- Added diagnostic logs for snapshot requests, server snapshot counts, client snapshot application, and empty destination lists.
- No save reset is required.

## 0.4.12 - 2026-05-17 22:06 JST
- Improved trader destination labels with friendly trader names, distance, direction, and coordinates.
- Destination choices are now ordered by distance from the player.
- Reworked the Nexus Mods description to clarify that configuration is mainly for multiplayer server access modes.
- Clarified that legacy TXT save migration applies to data from version 0.2.x and older.

## 0.4.11 - 2026-05-17 17:20 JST
- Added client-side destination visual warm-up, chunk visual refresh, and a short visual hold around teleports.
- Reduced cases where POI objects such as shutters or vehicles can remain transparent after teleporting even though their collision/state exists.
- No save reset is required.

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

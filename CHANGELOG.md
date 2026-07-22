# Travel Between Visited Traders Changelog

## 0.6.20 - 2026-07-21 01:00 JST
- Fixed the deeper causes of freezes after repeated or simultaneous trader travel: trips are serialized from destination preparation through arrival, with a bounded queue and timeout instead of piling chunk and mesh work onto the game at once.
- Hardened stalled-trip recovery: queued trips expire promptly, orphaned observers and pending state are cleaned up before another trip starts, and generation tokens prevent delayed cleanup from releasing a newer trip's slot.
- Re-check mesh-queue saturation around the charged teleport, and start destination visual pre-load only after server approval, reducing unnecessary mesh work and preventing paid travel from being abandoned after it starts.
- Fixed travel being refused almost every time a destination actually needed preparation: the mesh-queue busy check was measuring the load the trip's own destination preparation had just produced, since the preparation area wasn't released until after that check ran.
- No save reset is required.

## 0.6.19 - 2026-07-19 23:40 JST
- Backported the 0.7.6 fixes from the 3.0 line: reduced the load travel puts on the game's chunk and mesh pipeline (addressing reports of the game freezing after heavy teleport use in multiplayer), a "transport is busy" wait when the game's mesh queue is close to its limit, a short 10-second wait between trips per player, and a fix for server-sent notices never appearing for players connected to a server.
- No save reset is required.

## 0.6.18 - 2026-07-19 19:11 JST
- Fixed placed/drivable vehicles (minibike, motorcycle, bicycle, 4x4, gyrocopter, helicopter, blimp) still being dragged along to the trader on travel. The 0.6.17 fix only excluded vehicles by name and missed all of these; they are now excluded by their actual type, so travel only ever moves entities that are really your companions.
- No save reset is required.

## 0.6.17 - 2026-07-18 10:22 JST
- Fixed parked vehicles (and in rare cases other entities) being dragged along to the trader when you travel. This could happen when a leftover companion record from the SCore / XNPCCore hire system pointed at an entity id the game had since reused for something else, such as a vehicle in your garage. Travel now moves only entities that are really your companions; hired companions still come along as before.
- No save reset is required.

## 0.6.16 - 2026-06-12 03:08 JST
- In multiplayer, the travel confirmation prompt now follows the server config, matching how access mode and travel cost already work. Clients connected to a server use the server's confirmation setting instead of their own local one. Single-player is unchanged.
- No save reset is required.

## 0.6.15 - 2026-06-11 02:39 JST
- Reduced NPC companions being duplicated when you travel in modpacks that use the SCore / XNPCCore companion system (such as The Wasteland). Following companions are now moved off the departing area just before travel, so the area you leave no longer keeps a copy that reappears when you return. Only your own following companions are moved, and companions told to stay or guard are left in place. The underlying companion behavior is the framework's, not this mod's, and companions duplicated before this update are not removed automatically.
- No save reset is required.

## 0.6.14 - 2026-06-09 01:14 JST
- Rebuilt against 7 Days to Die v2.6 (b14) so the mod binary matches the current game build.
- No functional changes and no save reset is required.

## 0.6.12 - 2026-06-07 02:00 JST
- Reduced NPC companions being duplicated when you travel in modpacks that use the SCore / XNPCCore companion system (such as The Wasteland), by changing how the teleport moves the player. In testing companions no longer multiplied per trip, though the underlying companion behavior is the framework's, not this mod's.
- Following NPC companions are now pulled to you when you arrive and placed around you on solid ground, so they are less likely to be left buried in the floor or stuck in walls. Only companions that belong to you and came along are moved; this does nothing on setups without companions.
- No save reset is required.

## 0.6.1 - 2026-06-01 15:08 JST
- Fixed travel cost not being charged to multiplayer clients when the travel transition was turned off (`enabled="false"` or `durationSeconds="0"`). The cost is now taken on the traveling client regardless of the transport screen. Hosts and single-player were already charged correctly, and the has-enough-items check was unaffected. This applies to any cost item.
- No save reset is required.

## 0.6.0 - 2026-05-30 19:27 JST
- Added an optional confirmation prompt before traveling, so an accidental click no longer moves you (and spends travel-cost items) immediately. After picking a destination you get a "Travel to X?" screen with Yes/No; No returns to the list.
- Configurable in `VisitedTraderTeleport.xml` with `<Confirmation mode="..." />`: `off` keeps instant travel, `always` always asks, and `whenCost` asks only when the trip costs items. The default is `whenCost`.
- Confirmation is a per-player client-side preference; in multiplayer each player uses their own setting.
- Destination entries now always show name, distance, direction, and coordinates, instead of dropping the coordinates on paid trips to fit the cost in. The exact cost is shown on the confirmation screen, and the per-distance rate stays in the destination screen header.
- Destination entries now also show the biome of each trader (forest, desert, snow, wasteland, burnt forest), since trader locations vary by world. The biome is captured when you visit a trader and stored with the visit, so it shows correctly for far traders too; traders already visited before this update fill in their biome the next time you talk to them.
- No save reset is required.

## 0.5.26 - 2026-05-28 04:45 JST
- Fixed the client-side travel-cost check so the configured cost is actually applied when starting a trip; previously the check used an empty default and let the trip proceed even when the player had no gas.
- Travel overlay now stays visible after the configured transition duration until the destination chunk is loaded on the client, so the trader you arrive at is rendered before the arrival message appears.
- Capped the additional overlay hold to 15 seconds so a missing or slow-loading area cannot lock the overlay forever.
- No save reset is required.

## 0.5.25 - 2026-05-28 04:06 JST
- Fixed travel-cost consumption on dedicated servers so the configured item is actually deducted from the player's inventory after a paid trip.
- Travel-cost item count is now checked and consumed on the client; the server no longer tries to mutate a remote player's inventory directly.
- Added a client-side travel-cost pre-check so insufficient-cost feedback appears immediately when the trip is chosen, instead of relying on the server's view of the inventory.
- Extended the destination preparation timeout from 8 to 12 seconds for slower dedicated-server chunk loads.
- No save reset is required.

## 0.5.24 - 2026-05-28 00:45 JST
- Fixed travel-cost consumption to use the multiplayer inventory update path so paid transport applies item removal more consistently.
- Added extra server-side logging around removed item counts and post-payment inventory totals for easier dedicated-server diagnosis.
- No save reset is required.

## 0.5.23 - 2026-05-27 20:03 JST
- Fixed dedicated-server travel cost checks using the same player inventory and bag item-count APIs used by other server-side mods.
- Fixed travel cost consumption to use the matching server-side item removal calls.
- No save reset is required.

## 0.5.22 - 2026-05-27 09:10 JST
- Prepare unloaded destinations asynchronously on dedicated servers before charging and starting transport, instead of immediately blocking the trip.
- Keep the no-charge timeout fallback if the server cannot prepare the destination safely.
- No save reset is required.

## 0.5.21 - 2026-05-27 09:00 JST
- Block dedicated-server transport before charging if the destination chunk is not already loaded, avoiding unsafe teleports into missing chunks.
- Restored asynchronous destination preparation for local games so unloaded destinations are prepared before travel cost is consumed.
- No save reset is required.

## 0.5.20 - 2026-05-27 07:18 JST
- Retry multiplayer package registration after the game rebuilds its base net package mapping, improving dedicated-server reliability.
- Added clearer server-side travel-cost logs showing required cost and inventory/bag counts when transport is allowed or blocked.
- No save reset is required.

## 0.5.19 - 2026-05-27 06:05 JST
- Show the mode, page, and travel-cost rate only on the destination selection screen, keeping the main trader dialog cleaner.
- Fixed multiplayer package registration so server-side transport requests, visited-trader snapshots, and travel-cost settings can sync on game builds with a smaller net package ID range.
- No save reset is required.

## 0.5.18 - 2026-05-27 03:49 JST
- Restored the full main trader dialog label for requesting transport.
- Moved mode, page, and travel-cost rate details into a separate status row so the main transport choice is less likely to be clipped.
- Fixed the transition overlay text layout so long transport messages do not overlap the wait hint.
- Stopped overriding trader dialog headings so the current trader name does not carry over after transport.
- No save reset is required.

## 0.5.17 - 2026-05-27 03:08 JST
- Shortened transport choice and travel cost labels so Japanese item names are less likely to be clipped in the trader dialog.
- Fixed trader dialog headings so localized trader names and the current access mode are shown consistently on both the main and destination screens.
- Expanded the travel overlay departure message to show the trader destination and coordinates.
- No save reset is required.

## 0.5.16 - 2026-05-27 02:41 JST
- Fixed long-distance transport failing when the destination chunk preparation never reached the game's ready state.
- Made the destination screen update the visible response-list heading directly so the current access mode and page status are shown reliably.
- No save reset is required.

## 0.5.15 - 2026-05-27 01:54 JST
- Renamed the trader dialog entry to better match the transport-service style of travel while keeping the label short enough for the dialog panel.
- No save reset is required.

## 0.5.14 - 2026-05-27 01:38 JST
- Shortened travel-cost labels in trader dialog choices so Japanese and other localized item names are less likely to be clipped by the game's dialog panel.
- No save reset is required.

## 0.5.13 - 2026-05-27 00:43 JST
- Removed the `disableCamera` transition config option; travel now uses the full-screen transition without exposing a separate camera toggle.
- Added the configured travel-cost rate to the main "Travel to a visited trader" dialog choice.
- Travel cost labels now prefer the game's localized item name, with `displayName` kept only as a fallback for custom items.
- Clamped invalid travel-cost and transition config values during load so negative, zero, or extreme numbers do not produce unsafe behavior.
- No save reset is required.

## 0.5.12 - 2026-05-27 00:29 JST
- Restored the destination screen access mode and page status by applying the response-list heading patch to the base dialog binding method used by the game.
- Stopped directly toggling the player camera during travel so `disableCamera` no longer flips camera-control behavior while the overlay is active.
- No save reset is required.

## 0.5.11 - 2026-05-27 00:08 JST
- Blocked player control and overlay input during the travel transition so the trip cannot be played through like a normal dialog moment.
- Added `soundRepeatSeconds` to keep the configured travel sound active during longer transitions.
- No save reset is required.

## 0.5.10 - 2026-05-26 20:08 JST
- Added a full-screen travel overlay while trader transport is preparing and in progress, then clears it before showing the arrival message.
- No save reset is required.

## 0.5.9 - 2026-05-26 19:58 JST
- Fixed a startup error when the optional destination-heading patch could not find the response-list binding method in the running game build.
- No save reset is required.

## 0.5.8 - 2026-05-26 05:04 JST
- Smoothed the travel transition so the default trip keeps the camera visible, shows clearer departure and arrival messages, and avoids stacking the generic teleport tooltip on top of the transport message.
- Changed the sample transition config to `disableCamera="false"`; existing configs keep their current setting.
- No save reset is required.

## 0.5.7 - 2026-05-26 04:49 JST
- Fixed the destination screen heading patch so mode and page status are applied to the response-list heading used by the trader dialog.
- No save reset is required.

## 0.5.6 - 2026-05-26 02:48 JST
- Moved destination mode and page status into the trader name heading on the destination screen so the selectable list stays limited to real choices.
- No save reset is required.

## 0.5.5 - 2026-05-26 02:14 JST
- Fixed destination screen status text so the current access mode and page count are shown above the selectable choices again.
- Changed the sample gas travel cost to `perMeter="0.1"` and added a Duke's casino token example using `casinoCoin`.
- No save reset is required.

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

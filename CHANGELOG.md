# Changelog

All notable Pelican Companions changes are documented here.

## 1.5.1 — 2026-07-18

### Follow and recall performance

- Made the `X` wheel and quick-HUD Follow command state-only on the input frame:
  task/directive cleanup remains immediate, while tile selection and path
  creation are deferred to the host's follower update.
- Removed full-map reachability scans from normal formation, breadcrumb-trail,
  recall-target selection, and the duplicate preflight before Stardew's path
  controller. Repath attempts are also less frequent.
- Replaced eager disconnected-map checks with targeted, early-exit probes that
  run only after sustained lack of progress and respect a cooldown. Bounded
  searches that end inconclusively can never authorize repositioning.
- Temporarily backs off endpoints rejected by Stardew's real pathfinder and
  rotates to another safe tile, preventing custom-map collision differences
  from retrying the same impossible route forever.
- Preserved conservative movement rules: ordinary same-map follow never
  teleports, cross-map transfer still requires a nearby safe tile, and explicit
  recall fallback requires repeated definitive disconnected observations.
- Added regression coverage for recall reset policy and tick-safe connectivity
  throttling and controller preservation; the automated harness now contains
  21 tests.

## 1.5.0 — 2026-07-17

### Empty-ground squad orders

- Pressing `X` over a safe empty ground tile now opens a contextual wheel with
  Dismiss all plus up to three named local companions.
- A named order sends that companion to the marked tile and changes them to
  Waiting only after arrival; the saved waiting position then survives reloads
  and day changes.
- Added confirmation before Dismiss all and kept inventories, ownership, and
  vanilla schedule restoration on the existing safe dismissal path.
- Ground destinations reject occupied, cropped, blocked, actionable, warp, or
  visually covered tiles. Destinations, ownership, range, reachability, and
  stand reservations are revalidated by the host for multiplayer requests.
- Move-to-wait orders are transient and independent from the global task toggle:
  `F8` doesn't cancel an active move, while path failure stops the companion at
  their current safe position instead of teleporting them or leaking a
  reservation.

## 1.4.0 — 2026-07-17

### Quick HUD polish

- Moved the companion dock to the left by default, including a one-time config
  migration, while preserving the GMCM side selector.
- Redesigned the dock with a compact squad header, warm parchment cards,
  framed portraits, clearer status accents, level/full-inventory badges,
  labeled Work/Stop and Follow controls, and improved hover feedback.
- Kept the compact responsive layout for narrow and split-screen viewports,
  and moved opposite-side notices below the vanilla top-right HUD.

### Context-sensitive command wheel

- Replaced the nearest-companion wheel with exact cursor hit-testing. Hovering
  an owned companion now shows Profile, Work, Stop, Dismiss, and Follow; an
  unrecruited NPC shows Recruit; another player's companion remains protected.
- Added variable one-to-five-sector rendering, per-action colors, target-aware
  titles, separator dead zones, and regression coverage for variable radial
  layouts.
- Hovering a mature untapped wild tree, breakable stone, or mature grab-harvest
  crop now offers Send all plus up to three named local companion choices.

### Direct group work

- Added host-authoritative contextual task requests with location, tile, target
  kind, token validation, and runtime instance identity so stale clicks and
  resources replaced during pathing fail closed.
- Added explicit direct work which can replace a selected companion's current
  task and uses a safe basic axe/pickaxe without requiring the farmer to equip
  one. The one-shot order bypasses disabled task modes without changing the
  owner's global task toggle.
- Added a read-only prepare phase for target/stand reservations, so an invalid
  direct order doesn't erase the companion's previous task.
- Added shared target cohorts: several companions can reserve unique adjacent
  positions and contribute hits to one tree or stone. Atomic crop harvest ends
  peer tasks as a successful group completion instead of false target-loss
  failures.
- Preserved flower protection and the existing farmhand-crop safety restriction:
  Stardew's crop API would credit the host, so remote crop commands explain the
  limitation instead of mutating ownership incorrectly.

## 1.3.0 — 2026-07-17

### Radial quick actions

- Added a configurable `X` quick-action wheel centered around the mouse. It
  targets the player's recruited companion nearest the cursor on the current
  map and exposes Menu, Work, Wait here, and Follow in four quadrants.
- Added hover highlighting, companion-name context, viewport clamping for small
  windows/split-screen, outside/dead-zone cancellation, and English/PT-BR text.
- Made the overlay modal while open, so its key and clicks cannot also trigger a
  vanilla action, tool use, or another companion surface.
- Reused host-authoritative idempotent multiplayer commands for farmhands and
  fixed Work after Wait: preserved work directives no longer prevent the NPC
  from resuming or re-enabling tasks.
- Corrected the quick HUD's paused-work indicator after Wait, made global task
  re-enabling explicit in feedback, and rejected stale remote Wait commands if
  the NPC changed maps before the host processed them.
- Added regression coverage for all four radial hit regions, the center dead
  zone, outer bounds, separator gaps, and invalid coordinates.

## 1.2.0 — 2026-07-17

### Runtime reliability

- Made multiplayer simulation gates owner-aware, so a menu/event on the host
  pauses only the host's companions while remote owners keep simulating; stale
  path controllers are detached while an owner is locally paused.
- Fixed Recall declaring success across fences or walls, empty off-screen path
  controllers remaining attached, and successful recovery paths retaining a
  false `stuck` status.
- Converted repeated schedule suppression into an idempotent NPC control lease
  and extended the structural reachability cache to a short bounded lifetime,
  removing reflection-heavy cleanup and full BFS scans from the hot path.
- Replaced the global Harmony release depth with a reentrant allowance scoped to
  each NPC, preventing one companion's vanilla restoration from releasing
  another companion's movement guards.

### State, saves, and multiplayer integrity

- Save schema 8 captures and restores each NPC's original base/added movement
  speed, including migration for active companions and deferred restores from
  older saves. Missing custom NPCs now always retain a deferred restore intent.
- State snapshots are fully validated, cloned, normalized, and materialized
  before replacing the client's last known-good state; their revision is only
  committed after the complete apply succeeds.
- Save/snapshot construction no longer mutates live members or UI previews, so
  one revision always represents a stable payload.
- Remote item withdrawal now verifies a deterministic fingerprint covering ID,
  stack, quality, parent, color, and sorted mod data. Stale clicks cannot remove
  a different stack.
- Replaced state-dependent remote toggles with idempotent `Set` commands, added
  bounded per-player replay protection, and routed command results/errors back
  to the requesting farmhand instead of the host's HUD.
- Made the GMCM compatibility interface public, fixing SMAPI's API mapping error
  observed in the runtime log.

### Validation

- Added a dependency-free console regression harness with 17 tests for config
  normalization, progression, legacy refunds, GMCM visibility, command replay,
  and serialized item identity.
- `scripts/validate.sh` now restores clean worktrees, builds the mod and harness,
  runs all tests, and validates JSON plus English/PT-BR key/token parity.
- Live in-game and co-op QA is still required before publishing this release.

## 1.1.2 — 2026-07-12

### Companion control and movement

- Centralized ownership of follow, recall, and task path controllers so vanilla
  routes can no longer masquerade as a companion controller or overwrite an
  active order.
- Fully suspends temporary schedule routes, walking-square returns, route-end
  animations, movement freezes, spouse return-home behavior, and pet roaming
  while an NPC is recruited.
- Blocks pet location-entry sleep, push trajectories, bowl/home warps, and
  behavior movement while the companion controller owns the pet.
- Companion path creation now preserves the spouse's daily marriage dialogue
  instead of letting Stardew's default path constructor clear it.
- Replaced Stardew's off-screen path teleport with a pausing companion
  controller and restricted same-map emergency repositioning to explicit recall.
- Separated structural reachability from temporary character occupancy, so a
  farmer, animal, or NPC standing in a doorway doesn't make the map look
  permanently disconnected.
- Passable flooring, tilled soil, bridges, and NPC doors now match the game's
  pathfinding rules instead of being treated as walls by the preflight scan.
- Keeps healthy task routes instead of recreating them every 30 ticks, and plans
  autonomous work before the single follower navigation pass.

### Task and schedule reliability

- Task timeouts now measure lack of progress; walking and successful tool hits
  refresh the budget instead of valid long-distance work expiring mid-action.
- Companions must reach the exact reserved stand tile before acting, preventing
  tool use from one tile short and multiple Recall targets from overlapping.
- Failed autonomous wood/mining targets receive a short retry backoff instead of
  being selected in an immediate work/follow loop.
- Waiting and disconnect parking now cancel every movement source and retain a
  mode-consistent activity state.
- Dismissal reloads today's vanilla schedule and restores the NPC to its current
  scheduled stop, rather than clearing the schedule and checking an empty one.
- A dismissal during scheduled travel keeps the live pre-reload position and
  pauses the remaining route for that day instead of teleporting to an old stop.
- Married home/bed endpoints are resolved from the internal Bus Stop waypoint
  to the real farmhouse routine (kitchen before bedtime, bed afterward); pet
  dismissal also respects its home, owner, behavior, and night routine.
- Daily control is reacquired only after vanilla `OnDayStarted`/marriage duties;
  spouse patio animation, bedtime state, and bed mutex ownership are restored
  without letting the base routine overwrite a companion order.
- Save schema 7 records the original schedule key so dismissal preserves the
  exact daily/rain/island schedule without rerolling it.

## 1.1.1 — 2026-07-11

### Multiplayer integrity

- Made the host the sole authority for follower controllers, task queues,
  reservations, world mutations, XP, skills, directives, and inventories.
- Routed lifecycle, recall-all, task toggles, manual/mimic actions, quick work,
  directives, skills, and every withdrawal through idempotent farmhand commands.
- Added revisioned state snapshots on join, after commands, and periodically for
  repair; farmhands no longer read/write SMAPI save data directly.
- Protected the host's shared state from split-screen lifecycle/snapshot events
  and isolated quick-HUD geometry/cache per local screen.
- Spatial farmhand commands now carry their source map and blocked-state queues
  expire stale requests before execution.
- Preserved an explicit Waiting order across disconnect/reconnect and deferred
  only the vanilla schedule restoration when events/festivals make it unsafe;
  the dismissal and inventory commit happen immediately and survive saving.
- Declared host gameplay settings authoritative and replicate the rules needed
  by remote panels and previews.

### Behavior and recovery

- Fixed recall loops across fences by using its real 1.5-tile arrival radius and
  restricting recovery placement to the owner's reachable component; autonomy
  and map transitions can no longer cancel the recall early.
- Reserved work targets, work standing tiles, and follower destinations against
  each other so companions don't converge on the same empty tile.
- Timeouts now count only active task ticks; time spent in menus/events no longer
  expires queued work.
- Isolated each task/follower update from custom-content exceptions and always
  releases its reservations on failure.
- Distributed autonomous care across all available companions instead of always
  returning after the first assignment.
- Lumber mimic now chooses a different nearby tree instead of racing the
  player's aimed tree, and input events no longer perform an immediate axe hit.
- Moving animal targets update their displayed coordinates; collision caches are
  invalidated after forage, tree, or stone removal.

### Saves, inventory, and interface

- Save schema advanced to version 6. Item stacks now also preserve colored-item
  tint and preserved-parent identity, while pending safe schedule restores are
  persisted across shutdowns.
- Invalid/duplicate save entries are rejected transactionally. A failed or
  future-schema load makes the mod inert for that session, informs farmhands,
  and never overwrites the original or mutates the world against empty state.
- Temporarily missing custom NPCs retain ownership, progression, directives,
  position, and inventory instead of being destructively removed.
- Withdrawals and task rewards commit stack-by-stack and reconcile partial
  custom-item failures, preventing duplicated sources or lost remainders.
- Remote single-stack withdrawals use a stable saved index and expected item ID,
  avoiding stale/filtered UI index mismatches.
- Fixed every panel tab at split-screen/high-UI-scale heights, added an
  ultra-compact Skills layout, truthful disabled-progression states, and
  inventory display caching.
- GMCM now uses its native keybind picker through a local compatibility API, so
  the declared SMAPI 4.0 minimum remains valid; null keybinds and invalid enum
  values in `config.json` are normalized safely.
- Corrected skill descriptions and the configurable quick-HUD side text.

### Known limitation

- Farmhand-owned crop harvesting is disabled: Stardew Valley 1.6's public crop
  transaction is tied to `Game1.player`, which would credit the host's inventory,
  XP, and professions. Other remote companion tasks remain host-authoritative.

## 1.1.0 — 2026-07-11

### Architecture

- Split the 4,654-line `ModEntry.cs` into focused lifecycle, recruitment,
  following/navigation, task, management, inventory/progression, dialogue,
  persistence, and configuration modules. The SMAPI entry point is now under
  200 lines.
- Centralized save-scoped runtime cleanup so state can't leak between farms
  after returning to the title screen.
- Added maintained architecture, extension, and manual-QA guides plus a single
  validation script for build, JSON syntax, and translation parity.

### Interface

- Replaced the left-side card stack with a compact grouped dock (58px detailed
  rows or 46px compact rows), accurate hitboxes, cached portraits, explicit
  direct/autonomous work states, and configurable left/right placement.
- Rebuilt the companion panel around responsive Overview, Work, Skills, and
  Inventory tabs with wide/narrow layouts, resize handling, whole-row scrolling,
  keyboard/gamepad focus, stable skill branches, and clearer destructive actions.
- Moved target-preview calculation out of the render path and refresh it on a
  bounded tick cadence while the panel is open.

### Behavior and safety

- Watering, gathering, harvesting, and petting now use the same queued
  approach/revalidate/commit flow as long-running work; companions no longer
  alter the world before reaching a safe adjacent tile.
- Pending work now resolves the task owner's map and equipped tool. Axe and
  Pickaxe instances are cloned before companion use.
- Added conservative same-map recovery for disconnected path regions after
  repeated reachability/progress observations.
- Added target reservations for short actions, per-kind task-disable gates,
  owner-aware forage/bonus routing, and XP for watering/gathering.
- Automatic/mimicked short tasks no longer flood the normal HUD with completion
  messages.

### Saves and compatibility

- Save schema advanced to version 4. Item stacks now preserve `modData` in
  addition to qualified ID, stack, and quality.
- Unavailable custom items and partial withdrawals remain stored instead of
  being silently deleted; visible inventory indices are mapped back safely.
- Dismissing a companion, including the silent disconnect path, now moves every
  carried stack through persistent overflow/shared storage before removal.
- Transient work/target state is cleared during load, preventing ghost
  "working" statuses without a backing task.
- Removed the nonfunctional Combat branch from the panel and refunds points
  spent on its legacy skill IDs when an older save loads.
- Companion dismissal restores the NPC to its captured pre-recruitment position
  before vanilla schedule recovery when that position exists in the save.
- Nonfunctional configuration switches remain readable for backwards
  compatibility but are no longer advertised through GMCM.

### Validation

- `dotnet build --no-restore`: 0 warnings, 0 errors.
- JSON syntax and English/Brazilian Portuguese key parity validated.
- Live in-game QA remains required; see `docs/MANUAL_QA.md`.

## 0.2.0 — 2026-07-10

### Added

- Adaptive companion formation for new configs, with breadcrumb following while moving and a readable settled formation while stationary.
- Manual, mimic, and autonomous mature grab-crop harvesting.
- Configurable beehouse flower protection for companion harvesting.
- Manual, mimic, and autonomous petting of nearby unpetted animals.
- Manual and player-tool mimic mining with an equipped pickaxe.
- Configured autonomous tree/mining scans when those modes are set to Autonomous, without requiring an active panel directive.
- Preferred per-companion work specialty remembered by the quick-work toggle.
- One-time final-level skill point for companions reaching or already at level 10.
- Reconnect resume for companions parked by a multiplayer owner disconnect.
- Save restoration of waiting and disconnect-parked locations/tiles.
- Day-start cleanup/restoration which cancels leftover work, clears trails, restores Waiting/Parked tiles, and reapplies companion schedule locks after the vanilla reset.
- Short-lived reachability and target-preview caches.
- Complete key-matched Brazilian Portuguese localization alongside the normalized default English text.
- Bounded host-side deferral for farmhand action requests received during unsafe/blocked game states (64 queued, up to 8 processed per safe update).

### Changed

- Tree and breakable-stone work now delegates ordinary world mutation and drops to Stardew Valley's public tool/object APIs.
- Autonomous target selection enumerates real tree/object candidates instead of scanning every tile in the work square.
- Wood targeting is limited to mature, untapped, non-stump trees.
- Disabling tasks cancels current work; disabling a task's directive cancels and releases its queued target.
- Disconnecting an owner cancels pending work before dismissing or parking companions.
- Quick-work resumes the companion's remembered Wood, Mining, or Clear Area specialty instead of always selecting Clear Area.
- Existing formation choices are preserved during config migration; Adaptive is the default only for newly generated configs.
- NPC profile data from Pelican's current public asset and the legacy public asset key are merged by profile/category with duplicates removed.
- Save member display names refresh from current NPC data.
- Temporarily unresolved custom items remain in persisted overflow until their defining content becomes available again.
- Save schema advanced to version 3 and config schema to version 4.
- Skill/directive panel layout adapts its columns and uses a single directive row when space permits.
- Selected-companion management in the F9 panel now includes direct Wait/Resume, Recall, and Dismiss buttons.
- Very short panel layouts retain five compact Skills/Inventory/Wait/Recall/Dismiss controls and use denser inventory spacing.
- Fresh configs start Watering, Lumbering, Mining, Harvesting, and Petting in Mimicking; Foraging and the unfinished task modules remain disabled.
- Quick-HUD state/hover text distinguishes configured Autonomous work from a manually toggled specialty and lets the work button switch cleanly between them.

### Fixed / hardened

- Quick-HUD click handling now uses scaled screen coordinates.
- Companion hotkeys and mimic actions are gated before they can replace active game menus or run during events, festivals, transitions, or minigames.
- Recruitment eligibility is revalidated when the confirmation response is committed.
- Tapped trees are rejected both during target selection and immediately before task execution.
- Missing saved NPCs no longer leave invalid squad members; recoverable carried stacks are moved to overflow/shared storage.
- Pending tasks and target previews are invalidated more consistently when task/directive state changes.

### Known limitations

- Manual in-game QA for 0.2.0 has not yet been run.
- Multiplayer is experimental and does not yet provide rich result/state synchronization or complete remote panel/inventory workflows.
- Combat, fishing, shearing, milking, sitting, riding, scythe harvesting, custom task/idle sprite playback, companion deposits, and chest-style inventory UI remain unimplemented.

# Changelog

All notable Pelican Companions changes are documented here.

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

# Changelog

All notable Pelican Companions changes are documented here.

## Unreleased

### Companion panel visual redesign

- Refreshed the F9 companion panel with the visual language shared by AliveNPCs
  and Beach Episode: the native warm-yellow Stardew parchment, a centered
  title/subtitle, status-accented cards, gold tabs with green selection, and
  semantic green or coral action buttons.
- Rebalanced type sizes and control spacing throughout the panel. The routine
  editor now uses concise activity labels and a 5-by-4 hourly grid when vertical
  room is available, while retaining its dense fallback for compact viewports.
- Increased the hierarchy of hours, levels, counters, and progression totals
  without enlarging surrounding labels. Dense routine cells now prioritize a
  readable hour instead of squeezing the hour and activity into one tiny line.
- Responsive and compact layouts still preserve every tab and action, including
  mouse, keyboard, and controller focus/navigation; the subtitle appears only
  when the viewport has room for the expanded header.

### Chest destination experience

- Reworked the chest destination panel with native Stardew parchment, a clear
  instruction header, stronger hover/selection states, readable pagination, and
  distinct indicators for explicit, inherited, and other-chest assignments.
- Compact pagination now also protects low-height viewports from unusably short
  rows. Farmhand assignment controls remain visibly locked while the host is
  confirming a newly identified chest, preventing accidental duplicate intents.

### Permanent NPC progression profiles

- XP, level, unspent points, unlocked skills, and recent-loot history now live
  in a permanent profile keyed by the NPC, independently of active squad
  membership. Dismiss and dismiss-all remove control and cargo without erasing
  progression; recruiting that NPC again reattaches the same profile.
- Schema 11 migrates progression from legacy active-member records into the
  permanent profile without carrying ownership, inventory, movement, or work
  orders. These profiles remain in schema 13 saves and multiplayer snapshots
  even while their NPC is outside the squad.

### Owner-scoped companion equipment

- The Inventory tab now exposes dedicated Axe, Pickaxe, Watering Can, and
  Fishing Rod slots. A matching selected toolbar tool equips or swaps directly;
  an empty selected toolbar cell removes it. Tools never enter companion cargo
  or chest routing.
- Equipment is stored per owner/NPC pair and survives dismissal and
  re-recruitment without transferring to another farmer. Axe/pickaxe upgrades,
  watering-can water, and safe tool `modData` round-trip through schema 13;
  legacy rods are migrated out of ordinary cargo.
- Every equipment swap and watering charge also refreshes an owner-scoped
  checkpoint in `Farmer.modData`. Because it shares the vanilla inventory save
  transaction, a failure writing the separate mod payload can no longer lose or
  duplicate a tool. Temporarily unavailable legacy tools remain recovery-bound
  to their original owner instead of becoming shared loot.
- Lumbering, mining, watering, and fishing now require their corresponding
  equipped tool. Watering consumes the equipped can's persisted water and stops
  with a readable empty-can state until the player removes, refills, and
  re-equips it.

### Hourly companion routines

- Painting an hourly cell or using the 06–18 shortcut now activates the draft,
  so a newly filled grid can no longer save successfully while remaining
  silently inert. An active routine also owns the companion before generic
  configured autonomy, keeping Follow blocks and Follow completion in control.
- Water, Wood, Mine, and Clear now each have an independent work scope in the
  routine editor. Free Area covers the entire main farm; Delimited Area opens a
  construction-style farm view for placing an exact square from 3 × 3 through
  41 × 41 tiles, clamped to vanilla or custom farm-map borders.
- The area picker supports mouse, keyboard, and controller camera/cursor input,
  size controls, confirm, and cancel. Returning restores the previous location,
  farmer, HUD, and viewport as well as the same panel draft.
- A scheduled work block without a scope now remains paused and retries until
  the player explicitly chooses Free Area or Delimited Area (or supplies a
  compatible circular manual preset). It never silently becomes farm-wide. If
  an older save had already completed that block for the missing preset,
  defining its area resets the stale completion once.
- Temporary manual tasks and areas now return to the already-applied Follow,
  Wait, original-routine, or completion state when they end. Remembered work
  circles also respect a later radius reduction, and disabled task modes report
  that pause instead of incorrectly claiming that a tool is missing.
- Added a fifth Routine tab with twenty hourly cells from 06:00 through 01:59.
  Each cell can Follow, Wait, use the NPC's original routine, Water, cut Wood,
  Mine, Clear, or Deposit; a 06–18 shortcut fills a full work shift and applies
  the selected Follow/Wait/original completion behavior afterward.
- Routines can repeat daily or run only on the saved day. The host persists the
  applied day/block/revision key, so a contiguous work block applies once and a
  completed area does not restart every ten minutes or after reloading.
- Manual wheel areas remain circular. Schema 14 preserves existing circles by
  default when old JSON has no region discriminator, and those presets stay
  circular until explicitly replaced in the editor. A later manual circle does
  not overwrite an explicit Free/Delimited choice. Missing presets and disabled
  task modes remain paused for retry, while exhausted areas use the configured
  completion behavior.
- Routine edits are one host-authoritative compare-and-swap payload. Concurrent
  multiplayer edits are rejected instead of overwriting a newer grid. The host
  validates the main-farm identity and square bounds before accepting a scope,
  and execution state is never accepted from clients.
- Added `OriginalRoutine` as a real companion mode. It releases the mod's
  schedule locks and behavior patches while active, then safely reacquires
  control when a later block switches back to Follow, Wait, or scheduled work.

### Companion deposit chests

- Opening a normal player chest placed in the world now shows a side panel for
  assigning it to no companion, all companions, or individual companions. The
  host revalidates the owner, map, tile, chest type, and idempotent multiplayer
  command; global/special/fridge/gift chests are excluded.
- Assigned chests carry a stable GUID in `modData`, so a moved chest can be
  rediscovered across known locations and building interiors. Missing or
  duplicated GUIDs fail closed and retain the established inventory/world
  fallback route.
- Task loot tries the effective individual/owner-default chest before the
  existing companion, squad/player, and world endpoints. Partial stacks and
  exceptional custom items reconcile only their uncommitted remainder, and the
  routine deposit action leaves any cargo that cannot fit in the chest on the
  companion.
- A complete item-ownership checkpoint (per-companion cargo, shared squad
  inventory, and raw recovery overflow) is written into the host farmer's
  vanilla `modData` immediately before every save. It is restored before cargo
  normalization, so a valid checkpoint reconciles either side of the
  mod-payload/vanilla-save boundary without duplicating a deposited/withdrawn
  stack or losing its source state.
- A farmhand's first click on a chest without a GUID now performs an identity-
  only handshake. Assignment is sent only after the host ACK and the same GUID
  replicate onto the same still-open chest object; replacement and stale-token
  races fail without changing logistics.

### Watering work areas

- Fixed companions repeatedly watering the same tile. Target validation now
  distinguishes crops which require irrigation from soil which has already
  been watered, so the companion advances to the next dry tile.
- Added Water as a fourth persistent work-area specialty and a per-companion
  work directive. Companions reserve reachable dry soil inside the marked
  circle, water it with their equipped watering can, and keep working
  when their owner changes maps.
- Watering areas use the same host-authoritative save, multiplayer snapshot,
  target/stand reservations, blocked-area recovery, previews, and completion
  flow as Wood and Mining. This originally advanced the schema to 12; the
  combined operational-profile schema below is now 13.

### Directed companion fishing

- Fishing rods now use the dedicated owner-scoped equipment slot. Only a single
  unenchanted rod with no bait or tackle is accepted; the host revalidates the
  toolbar cell, previous slot fingerprint, and faithful serialized tool state.
- `X` on water now offers Send all fishing and named choices for local
  companions with equipped rods. The host discovers the exact cardinally connected
  water body, then assigns each worker the closest reachable safe shore with a
  distinct reservation, replan support, and no teleporting to another pond.
- A directed session continues through repeated catches until 26:00/day end or
  another command. Regular non-legendary fish use the assigned chest first,
  then companion/overflow/world routing, and grant 8 XP per fish through the
  existing progression system.
- Fishing commands reject non-fishable/`NoFishing` water. Catches use Stardew's
  data-driven fish selector directly, bypassing virtual location overrides that
  can consume one-time quest or team rewards before returning a non-fish item.
- Added Fishing as a fourth three-node skill branch: faster catches, one extra
  casting tile plus one quality tier, then a 25% chance of an extra fish. Pure
  water-body and session policies cover connectivity, shore/cast selection,
  timing, XP, quality, and bonus-catch boundaries in the regression harness.

### Persistent cosmetic hats

- Added a dedicated hat slot to each NPC companion's Inventory tab. A selected
  toolbar hat can be equipped or swapped atomically, while an empty selected
  slot removes the equipped hat back to the player.
- Hat ownership is stored separately from recruitment and ordinary carried
  items, so dismiss, dismiss-all, schedule restoration, day changes, and
  save/reload leave the NPC wearing it. This originally advanced the schema to
  10; the current combined schema is 13.
- Hat changes are host-authoritative in multiplayer; requests fingerprint both
  the selected toolbar hat and the cosmetic state shown by the client, while
  snapshots replicate the result. Missing custom hats remain preserved until
  their content pack is restored instead of becoming an invisible inventory item.
- Hats now follow the head movement drawn inside each walking frame. The offset
  is measured and cached from the NPC's own texture, so villagers that bob only
  in some directions (or don't bob at all) keep the correct alignment.

### Contextual resource reach

- Direct `X` commands now measure the three-tile safety limit against the
  companion's adjacent stand instead of the resource tile itself. A tree,
  stone, or crop one tile beyond that radius is accepted when its working side
  remains in range, with the same validation repeated by the host.

### Companion inventory routing

- Safe forage and the collectible item/resource debris created by companion
  lumbering and mining now enter that companion's inventory instead of bypassing
  it for the player/shared inventory or remaining scattered on the ground.
- A full companion inventory falls back to the configured shared squad inventory
  or owner inventory, then to a world drop. Mining uses a synchronous debris
  diff; a felled tree stays tied to the companion through its exact final
  `tickUpdate`, when the trunk drops actually appear. Existing drops, cosmetic
  chunks, archaeology, and essential items are left untouched.
- Added pure routing-order regressions; the automated harness now has 55 tests.

### Visible companion work

- Companions now face their target and show a short tool or hand motion only
  after reaching the reserved stand tile. Lumbering, mining, watering,
  gathering, harvesting, and petting each have readable visual feedback.
- Successful actions show a colored impact/check reaction; rejected actions
  show a shake and question reaction. These effects are cosmetic and never
  advance a task or mutate the world by themselves.
- The host replicates start/success/failure visuals to multiplayer clients so
  every player sees the same action without duplicating its gameplay commit.
- Lumbering/mining motions begin inside the existing hit cooldown, preserving
  their prior impact cadence while still committing only after the visible swing.

### Persistent work areas

- `X` on safe empty ground now offers Work, then Wood, Mining, or Clear Area,
  and finally Send all or a specific companion. The intermediate radius menu
  was removed; every new order uses the host-configured maximum radius and
  renders that 3–20 tile circle as a temporary boundary. Existing saved orders
  retain their recorded boundary until replaced, avoiding unexpected expansion.
- Area workers remain anchored to the selected map and center, accept only
  matching resources inside the inclusive circle, and may stand one adjacent
  tile outside it. Reserved or unreachable targets pause the order instead of
  falsely completing it.
- Bounded reachability checks may hand an inconclusive long route to the real
  path controller, and multiplayer snapshots preserve in-progress area previews
  and cosmetic work motions instead of cutting them short. Rejected remote
  orders clear their optimistic preview immediately, and failed placement is
  retried even when the NPC is already in the area's location.
- A truly exhausted area ends in Waiting with clear success feedback. The area
  intent survives save/reload while target, path, reservation, preview, and
  animation remain transient; this feature originally advanced the save schema
  to 9 (the current schema is 13). Reloading with tasks disabled
  preserves the paused state, and exhaustion still completes during placement
  recovery instead of retrying forever.

### Anti-repeat companion communication

- Replaced independent speech timers with a bounded queue per owner. Requests
  are deduplicated, expire by TTL, and prefer milestone/command/task reactions
  over ambient chatter while sharing one configurable group cooldown.
- Ambient speech rotates away from the previous speaker when possible. Recent
  lines are tracked for the whole group and each NPC, with the last four NPC
  identities persisted so reloads don't immediately restart the same phrases;
  per-line interval bookkeeping is bounded without evicting active intervals.
- Added communication and pet-expression settings to config schema 8.

### Silent but expressive pets

- Pets remain recruitable and fully manageable without ever receiving NPC text
  above their heads. Recruit, idle, success, failure, refusal, and dismiss
  intents become pet-appropriate emotes, sounds, hops, or shakes instead.
- Pet expressions use the same cooldown and host replication as other
  communication, preventing duplicate barks/emotes in multiplayer.

### NPC-specific contextual dialogue

- Dialogue resolution prefers an exact NPC profile, then type/villager and
  Generic fallbacks. Explicit fallback overlays can enrich an exact profile
  with shared season, weather, or friendship reactions; equally specific
  authored NPC lines still win before weighted anti-repeat selection.
- Recruitment from the `X` action wheel commits immediately after selecting
  Recruit, while the dedicated recruitment hotkey keeps its confirmation and
  refusal flow. Both paths remain host-authoritative for farmhands. Multiplayer
  speech is translated per client, with the host-resolved text retained if a
  locale lacks the key.
- Authored lines can react to friendship, spouse status, time/period, season,
  weather, indoor/outdoor location, map/context, task, manual orders, outcome,
  failure reason, and discovered item, with matching runtime tokens.

### Large-squad and controller action wheel

- Context and ground wheels now expose all eligible companions in squads of up
  to 12 through stable pages, while keeping global actions such as Send all,
  Work, and Dismiss all pinned and reachable.
- Added spatial focus navigation for arrows/WASD, D-pad, and both sticks;
  A/Enter activates, B/Escape cancels, shoulders/Page Up/Page Down or mouse
  wheel change pages, and modal input remains suppressed from the world below.
- Added pure regression coverage for pagination, focus navigation, dialogue
  scheduling/selection, and work-area geometry, radius, specialties, and saved
  state validation.

## 1.5.3 — 2026-07-20

### Quick HUD readability

- Reworked the side dock around the real vanilla font metrics, replacing the
  overlapping name/status/level stack with two properly spaced text lines.
- Removed heavy text shadows and impossible labels from the small action
  buttons; larger icon-only controls retain the existing localized tooltips.
- Added a short localized dock title, an open-panel chevron, and crisp custom
  pixel badges for level, member count, and full inventory state.

### Action-wheel readability

- Replaced the five-action wheel's forced `...` labels with fixed-size,
  multi-line captions, while preserving the existing wheel and hitbox geometry
  for narrow and split-screen viewports.
- Shortened the idle footer hint and added clearer caption backgrounds plus a
  highlighted border for the hovered action.
- Added regression coverage for localized one-word and multi-word wheel labels.

### Companion panel skill tree

- Replaced the Skills tab's uneven text-card grid with three responsive
  left-to-right progression lanes and compact tier nodes. Prerequisites now have
  visible connectors, branches have distinct accents plus progress counts, and
  every node communicates learned, available, locked, no-points, or disabled
  state without relying on color alone.
- Added a persistent side inspector with the complete skill name, branch, tier,
  cost, points, state, description, and action. Intermediate narrow layouts put
  the detail card below the tree, while compact split-screen/high-scale layouts
  use a bounded custom tooltip.
- Reworked the panel header, roster, tabs, badges, branch labels, and inspector
  around the vanilla fonts' measured line heights. Text now renders at a fitted
  scale without the heavy shadow that caused overlapping and vertical bleed;
  narrow viewports use localized short tab labels instead of clipped captions.
- Added skill-point and inventory tab badges plus first-focus/spatial D-pad
  navigation for the tree.
- Centralized skill availability in one policy shared by the panel and the
  host-authoritative unlock validation. The automated harness now has 31 tests.

### Distance-independent recruitment wheel

- Removed the leftover 2.25-tile proximity check from the final recruitment
  validation used by `X > Recruit`. An unrecruited NPC can now be recruited from
  anywhere on the player's current map when selected directly under the cursor.
- Kept host-authoritative checks for current map, supported NPC type, friendship,
  squad capacity, ownership, deferred restores, and safe game state. The legacy
  `F5` target selector remains intentionally proximity-based.
- Added a pure same-map recruitment policy and regression coverage.

## 1.5.2 — 2026-07-20

### Work performance and empty-ground commands

- Replaced each worker scan's bounded 2,048-tile reachability flood and repeated
  per-candidate checks with one early-exit search across all viable stand tiles.
  The selected stand is reused when the task is queued, and target preview no
  longer repeats the same scan.
- Removed repeated reachability floods from the five-tick work execution path.
  A valid reserved stand and its active controller are preserved until actual
  lack of progress requests recovery.
- Deferred task path construction out of the command/planning frame, limited
  new task paths to two per processing update, and delayed replanning after a
  resource is removed so several synchronous pathfinders can't stack in one frame.
- Bounded autonomous planning to three companions per scan with round-robin
  fairness and explicit-Work priority. Panel directive clicks now publish a
  lightweight planning state, directive hover no longer runs target search,
  and background panel previews use the same three-member budget.
- When Stardew rejects or stalls a stand path, the worker now excludes that
  side and tries another adjacent stand instead of retrying one endpoint forever.
- Removed the three-tile follow radius from empty-ground destinations and from
  the list of eligible local companions. Any safe, reachable cursor tile in the
  current map can now show the wheel and receive a named Move/Wait order.
- Kept multiplayer host validation for ownership, map, tile structure,
  occupancy, reservations, and a directed bounded reachability check before the
  real path controller. Added task-navigation, fair-planning, and ground-command
  regression policies; the automated harness now has 26 tests.

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
  task. The one-shot order bypasses disabled task modes without changing the
  owner's global task toggle; the current equipment system uses only the
  selected companion's own axe or pickaxe slot.
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

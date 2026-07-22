# Pelican Companions

Pelican Companions is an independent SMAPI companion mod for Stardew Valley 1.6. It lets players recruit eligible NPCs and pets, manage a temporary squad, use conservative companion task assistance, and develop each companion through levels, skills, and a small inventory.

Release documentation:

- [Architecture and extension guide](docs/ARCHITECTURE.md)
- [Current manual QA checklist](docs/MANUAL_QA.md)
- [Changelog](CHANGELOG.md)

## Identity and requirements

- Name: Pelican Companions
- Unique ID: `Lucas.PelicanCompanions`
- Author: Lucas
- Target: Stardew Valley 1.6 / SMAPI 4.0.0+
- Required mods: SMAPI only
- Optional: Generic Mod Config Menu
- Content Patcher: optional, not a manifest dependency

## Controls

| Default | Action |
| --- | --- |
| `X` | Open the contextual radial wheel for the NPC, resource, body of water, or safe empty ground under the cursor. |
| `Left Stick` | Open the same contextual wheel on a controller. Use either stick or the D-pad to select, `A` to confirm, `B` to close, and the shoulder buttons to change pages. |
| `F5` | Recruit/manage a nearby NPC or pet. |
| `F6` | Run an enabled safe manual task at the aimed tile. |
| `F7` | Open the shared squad inventory summary. |
| `F8` | Toggle companion tasks. |
| `F9` | Open the full companion panel. |
| Unbound | Recall all local companions; configure `RecallAllCompanionsKey`. |

All bindings can be changed in `config.json` or through GMCM.

## Current implemented scope

- Recruit an NPC selected with the `X` wheel from anywhere on the current map;
  friendship, capacity, ownership, NPC support, and safe-game-state checks still apply.
  Dismiss, wait, resume, and recall remain available for recruited companions.
- Natural follower pathing at a conservative fixed NPC speed, location-change placement, visible recovery, and Adaptive/Behind/Compact formations.
- Mouse/controller contextual wheel with pagination for groups of up to 12 companions. Owned companions expose Profile, Work, Stop, Dismiss, and Follow; unrecruited NPCs expose Recruit; mature trees, breakable stones, mature grab-crops, and water expose compatible group or named-companion commands. Safe empty ground can move a companion there, dismiss the group, or open the fixed-area work flow. Mouse wheel/Page Up/Page Down and controller shoulder buttons change pages; keyboard, D-pad, and either analog stick move focus.
- Directed companion fishing: use the Inventory tab to equip an NPC with an
  unenchanted fishing rod with no bait or tackle, then press `X` over fishable
  water to send one or all local
  rod holders. They walk to the closest reachable safe shore of that connected
  body of water and fish until 26:00/day end or another command. Each regular
  catch grants 8 companion XP and follows the configured route: assigned chest,
  NPC cargo, shared/player inventory, then a safe world drop.
- Persistent manual work areas: mark safe ground, select Wood, Mining, Water, or Clear Area, then choose one companion or the whole local group. Every new manual order automatically uses the host-configured maximum radius. Workers stay inside that circular area across saves until it is complete, paused, or explicitly replaced by another order.
- Per-companion hourly routines in the fifth panel tab: paint twenty cells from
  06:00 through 01:59 with Follow, Wait, Original routine, Water, Wood, Mine,
  Clear, or Deposit. Each work activity independently chooses either Free Area,
  which covers the entire main farm, or Delimited Area, which opens a farm view
  for placing a square from 3 × 3 through 41 × 41 tiles. Routines can repeat
  daily or run once and include a one-click 06–18 work shift. Painting activates
  the schedule; a missing work-area preset stays paused and retries, and is
  never silently treated as Free Area. Circular presets from older saves remain
  circular until the player replaces them with one of the new choices.
- Visible work feedback for every implemented task: companions face the target, swing an axe/pickaxe/watering can or use a hand gesture, then celebrate success or visibly react to failure. Cosmetic work state is synchronized to farmhands.
- Permanent per-NPC XP profiles with ten levels, skill points, a responsive
  four-branch Lumbering/Mining/Utility/Fishing skill tree, and recent-loot
  history. Dismissal does not reset progression; recruiting the NPC again
  restores the same level and unlocked skills.
- Per-member inventory receives safe forage, caught fish, and normal lumbering/mining drops;
  an assigned normal world chest is tried first, then overflow follows the
  configured shared-squad/player fallback and finally drops safely into the
  world. Open a placed chest to choose None, All companions, or individual
  companion destinations from its side panel; moved assigned chests retain a
  GUID identity.
- The Inventory tab has a dedicated cosmetic hat slot. Select a hat in the
  toolbar to equip or replace it; select an empty toolbar slot to take it back.
  Equipped hats stay on NPCs after they leave the squad and across save/reload.
- The same tab has four owner-scoped tool slots: Axe, Pickaxe, Watering Can,
  and Fishing Rod. Select a matching toolbar tool to equip/swap it, or an empty
  toolbar cell to remove it. Tools stay outside ordinary cargo and chest routes;
  watering consumes the equipped can's persisted water.
- Manual/mimic/autonomous support where applicable for watering, safe forage pickup, mature grab-crop harvesting, mature untapped tree chopping, breakable-stone mining, and animal petting.
- Beehouse flower protection and bounded per-member Wood/Mining/Water/Clear Area directives.
- Owner-scoped communication scheduling with a shared group cooldown, bounded priority queue, deduplication, and recent-line memory. Important task, loot, level, failure, and command reactions take precedence over ambient chatter.
- Personality-specific dialogue profiles for the 34 social vanilla NPCs, with contextual selection for friendship, weather, season, time, location, task, result, failure, and found items. English and Brazilian Portuguese text and optional GMCM registration are included.
- Pets remain silent: they respond through hearts/question marks, jumps, shakes, and their configured bark/meow/content sounds instead of speech bubbles.
- Namespaced save data with migration for older Pelican Companions states,
  including fixed-area orders, legacy circular routine presets, and recent
  dialogue memory.
- Host-authoritative multiplayer simulation: farmhands send idempotent commands, the host alone controls NPCs/tasks/world/inventories, and versioned snapshots keep remote HUDs, panels, directives, skills, and withdrawals synchronized.

Combat, shearing, milking, sitting, riding, custom idle sprite playback, and arbitrary player-to-companion cargo transfers are not implemented. Companions can deposit their existing cargo through assigned chests and the hourly Deposit routine; direct player transfers are limited to the cosmetic hat slot and the four dedicated tool slots. The chest-assignment side panel is currently mouse-only. Tool enchantments and fishing-rod bait/tackle are rejected because their state isn't yet persisted safely. Inert options and the unfinished Combat skill branch are intentionally hidden instead of being presented as working features. Multiplayer remains experimental until the manual co-op checklist is completed; crop harvesting by a farmhand's companion is conservatively disabled because Stardew Valley's crop API credits `Game1.player` instead of an explicit owner.

## Build

```bash
dotnet build -p:EnableModDeploy=false -p:EnableModZip=false
dotnet build -c Release -p:EnableModDeploy=false -p:EnableModZip=true
```

Expected release zip after the release build:

`bin/Release/net6.0/PelicanCompanions 1.5.3.zip`

## Verification status

Run `scripts/validate.sh` to restore/build the mod, execute the package-free
regression harness, validate all JSON files, and verify English/PT-BR
key and interpolation-token parity. The current in-game checklist still needs
to be run before release; multiplayer remains explicitly experimental until
that pass is complete.

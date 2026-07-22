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
- Directed companion fishing: use the Inventory tab to give an NPC an
  unenchanted fishing rod with no bait or tackle, then press `X` over fishable
  water to send one or all local
  rod holders. They walk to the closest reachable safe shore of that connected
  body of water and fish until 26:00/day end or another command. Each regular
  catch grants 8 companion XP and enters the NPC inventory first; configured
  overflow destinations and finally a safe world drop prevent item loss.
- Persistent fixed work areas: mark safe ground, select Wood, Mining, or Clear Area, then choose one companion or the whole local group. Every new order automatically uses the host-configured maximum radius. Workers stay inside the marked circle across saves until the area is complete, paused, or explicitly replaced by another order.
- Visible work feedback for every implemented task: companions face the target, swing an axe/pickaxe/watering can or use a hand gesture, then celebrate success or visibly react to failure. Cosmetic work state is synchronized to farmhands.
- Per-companion XP, ten levels, skill points, a responsive four-branch skill tree with useful Lumbering/Mining/Utility/Fishing effects, and saved recent-loot history.
- Per-member inventory receives safe forage, caught fish, and normal lumbering/mining drops;
  overflow follows the configured shared-squad/player fallback and finally drops
  safely into the world.
- The Inventory tab has a dedicated cosmetic hat slot. Select a hat in the
  toolbar to equip or replace it; select an empty toolbar slot to take it back.
  Equipped hats stay on NPCs after they leave the squad and across save/reload.
- The same tab has an explicit fishing-rod deposit action. It accepts only one
  selected, unenchanted rod with no bait or tackle and stores it in the NPC's ordinary
  persistent inventory; general player-to-companion deposits remain closed.
- Manual/mimic/autonomous support where applicable for watering, safe forage pickup, mature grab-crop harvesting, mature untapped tree chopping, breakable-stone mining, and animal petting.
- Beehouse flower protection and bounded per-member Wood/Mining/Clear Area directives.
- Owner-scoped communication scheduling with a shared group cooldown, bounded priority queue, deduplication, and recent-line memory. Important task, loot, level, failure, and command reactions take precedence over ambient chatter.
- Personality-specific dialogue profiles for the 34 social vanilla NPCs, with contextual selection for friendship, weather, season, time, location, task, result, failure, and found items. English and Brazilian Portuguese text and optional GMCM registration are included.
- Pets remain silent: they respond through hearts/question marks, jumps, shakes, and their configured bark/meow/content sounds instead of speech bubbles.
- Namespaced save data with migration for older Pelican Companions states, including fixed-area orders and recent dialogue memory.
- Host-authoritative multiplayer simulation: farmhands send idempotent commands, the host alone controls NPCs/tasks/world/inventories, and versioned snapshots keep remote HUDs, panels, directives, skills, and withdrawals synchronized.

Combat, shearing, milking, sitting, riding, custom idle sprite playback, and general companion inventory deposits are not implemented. Player-to-companion transfers are limited to the dedicated cosmetic hat slot and explicit deposit of a single unenchanted fishing rod without bait or tackle. Inert options and the unfinished Combat skill branch are intentionally hidden instead of being presented as working features. Multiplayer remains experimental until the manual co-op checklist is completed; crop harvesting by a farmhand's companion is conservatively disabled because Stardew Valley's crop API credits `Game1.player` instead of an explicit owner.

## Build

```bash
dotnet build -p:EnableModDeploy=false -p:EnableModZip=false
dotnet build -c Release -p:EnableModDeploy=false -p:EnableModZip=true
```

Expected release zip after the release build:

`bin/Release/net6.0/PelicanCompanions 1.5.3.zip`

## Verification status

Run `scripts/validate.sh` to restore/build the mod, execute the package-free
55-test regression harness, validate all JSON files, and verify English/PT-BR
key and interpolation-token parity. The current in-game checklist still needs
to be run before release; multiplayer remains explicitly experimental until
that pass is complete.

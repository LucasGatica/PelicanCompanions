# Pelican Companions

Pelican Companions is an independent SMAPI companion mod for Stardew Valley 1.6. It lets players recruit eligible NPCs and pets, manage a temporary squad, use conservative companion task assistance, and develop each companion through levels, skills, and a small inventory.

Release documentation:

- [Architecture and extension guide](docs/ARCHITECTURE.md)
- [Current manual QA checklist](docs/MANUAL_QA.md)
- [Nexus description for 0.2.0](docs/nexus-description-0.2.0.md)
- [0.2.0 manual release checklist](docs/manual-qa-0.2.0.md)
- [Implementation status](docs/implementation-status.md)
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
| `F5` | Recruit/manage a nearby NPC or pet. |
| `F6` | Run an enabled safe manual task at the aimed tile. |
| `F7` | Open the shared squad inventory summary. |
| `F8` | Toggle companion tasks. |
| `F9` | Open the full companion panel. |
| Unbound | Recall all local companions; configure `RecallAllCompanionsKey`. |

All bindings can be changed in `config.json` or through GMCM.

## Current implemented scope

- Recruit, dismiss, wait, resume, and recall companions with friendship, capacity, ownership, and safe-game-state checks.
- Natural follower pathing at a conservative fixed NPC speed, location-change placement, visible recovery, and Adaptive/Behind/Compact formations.
- Grouped quick-HUD dock (left or right, detailed or compact) plus a responsive F9 panel with Overview, Work, Skills, and Inventory tabs, keyboard/controller navigation, target previews, and direct management actions.
- Per-companion XP, ten levels, skill points, useful Lumbering/Mining/Utility skill effects, and saved recent-loot history.
- Per-member inventory and shared squad inventory with conservative fallback routing.
- Manual/mimic/autonomous support where applicable for watering, safe forage pickup, mature grab-crop harvesting, mature untapped tree chopping, breakable-stone mining, and animal petting.
- Beehouse flower protection and bounded per-member Wood/Mining/Clear Area directives.
- Data-driven dialogue profiles, English and Brazilian Portuguese i18n, and optional GMCM registration.
- Namespaced save data with migration for older Pelican Companions states.
- Host-authoritative multiplayer simulation: farmhands send idempotent commands, the host alone controls NPCs/tasks/world/inventories, and versioned snapshots keep remote HUDs, panels, directives, skills, and withdrawals synchronized.

Combat, fishing, shearing, milking, sitting, riding, custom task/idle sprite playback, and companion inventory deposits are not implemented. Inert options and the unfinished Combat skill branch are intentionally hidden instead of being presented as working features. Multiplayer remains experimental until the manual co-op checklist is completed; crop harvesting by a farmhand's companion is conservatively disabled because Stardew Valley's crop API credits `Game1.player` instead of an explicit owner.

## Build

```bash
dotnet build -p:EnableModDeploy=false -p:EnableModZip=false
dotnet build -c Release -p:EnableModDeploy=false -p:EnableModZip=true
```

Expected release zip after the release build:

`bin/Release/net6.0/PelicanCompanions 1.1.2.zip`

## Verification status

Run `scripts/validate.sh` to compile the mod, validate all JSON files, and verify English/PT-BR key and interpolation-token parity. The 1.1.2 automated validation completes with 0 build warnings/errors. The current in-game checklist has not yet been run; multiplayer remains explicitly experimental.

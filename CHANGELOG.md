# TerraStorage Changelog
## [0.2.8]

### Added
- **Sprite update**
    - All sprites have gotten a facelift. The CraftingCore and DriveBay are now different sizes, please break and replace your tiles to get the new form factor.
- **Drive Bay visual overlays** — small status lights on each disk slot show fill state (offline, online, 80% full, 100% full). Bay-level status light shows overall capacity across all disks at a glance.


### Fixed
- Removed some unused code.

## [0.2.7] - Released

### Added
- Cyrillic language support
  - NOTE: I've verified that you can type cyrillic characters in the search bar. I've added Russian language support though machine translation, but it's likely terrible.  Not all of the UX elements are translated or have Localization entries however, but this is a good start.

### Fixed
- Protect the floor/platform where a Crafting Core/Drive Bay contains items.
- Added simulated vanilla crafting mode (experimental, there will be bugs at the moment)
  - Brings support for mods that add data to items on craft
- Made the output slot size in the crafting tab larger
- Recipes that allow groups of items now display that ingredient as a group
- ItemGrid in crafting tab no longer jumps around when crafting

## [0.2.6] - Released

### Fixed
- Texture smearing on tiles (Terminal, CraftingCore, DriveBay)
- Fix stack merging issues caused by the previous patch.
- The collapsed browse pane in Encyclopedia no longer allows queries on an invisible item grid.

## [0.2.5] - Released

### Fixed
- Items with per-instance data from other mods (e.g. enchantments from Entropy) are no longer stripped on deposit into storage.
- Disk tier upgrades now preserve per-instance data (enchantments, GlobalItem state).

### Changed
- Network packets for unique items are now more compact — eliminated redundant ModData when FullItemTag is already present.

### Notes
- In MP, items with extra ItemTags do not load for the client unless the terminal is reopened/full sync occurs.  Small bug, it's only visual so impact is negligible.


## [0.2.4] - Released
**Small Height fix for Terminal**

### Changed
- The terminal max height is now able to be expanded to 80% of the screen height (Up from 60%)

## [0.2.3] - Released
**Massive UI Update**

### Added
- **Favorites toggle button** — a ★ button in the player inventory UI to open/close the Favorites panel from anywhere. Middle-click and drag to reposition it.
- **Defragment tooltip** — hovering the Defragment button in the Disks tab now shows an explanation of what it does.
- **Encyclopedia browse pane** — a collapsible item browser that slides in from the left edge of the Encyclopedia window, covering the detail panel. Contains the filter bar, sort bar, and item grid. Toggled by a permanent strip button on the left edge, or automatically when the search bar is focused. Clicking an item collapses it and shows the detail view.
- **Tooltip bleed prevention** — open windows (Encyclopedia, Terminal, Drive Bay, Crafting Core, Crafting Tree, Favorites) now block item tooltips from showing through them.

### Changed
- **Unified UI style** — all close buttons, tabs, and action buttons (Deposit All, Upgrade, Defragment) now share a consistent visual style.
- **Resize handle** — the corner resize handle on all resizable windows is now a diagonal-striped square instead of a solid block.
- **Deposit All** button moved above the item grid, aligned with the sort bar, to reduce wasted footer space.
- **Encyclopedia minimum size** reduced to allow much smaller window sizes.
- Reduced unused gap between the item grid and scrollbar in the Storage tab.
- Reduced excess right-side padding in the Crafting tab.

### Fixed
- **Smooth scrolling** — all scrollable lists and panels (Storage, Crafting, Encyclopedia, Disks) now scroll smoothly.
- **Disk tab FPS drop** — Improved rendering cost of viewing the Disks Tab.
- UI window positions and sizes (Terminal, Encyclopedia, Crafting Tree, Favorites panel) now correctly persist across game sessions.
- Drive Bay and Crafting Core windows now always open centered rather than restoring an off-screen saved position.

## [0.2.2] - Released

### Added
- Alt+click any item in the Encyclopedia or Crafting Tree to add/remove its recipe from Favorites.
- Alt+click a recipe in the Favorites panel to remove it.

### Fixed
- Crafting with a full storage network no longer silently destroys the crafted item — it goes to the player's inventory instead. If both storage and inventory are full, crafting is blocked.

## [0.2.1] - Released

### Added
- **Item Encyclopedia** (rebindable keybind)
  - Browse all items. Type `!` to switch to NPC browsing.
  - Detail panel shows crafting recipes, drop sources, shop sources, shimmer, and used-in recipes.
  - Recipes cycle with `<` / `>`. All icons are interactive: left-click to navigate, right-click for Crafting Tree, middle-click to send to Terminal.
  - Click an NPC to view their drops and shop inventory.

### Fixed
- Crafting Tree: selecting certain nodes (e.g. Lens) caused the mouse cursor to vanish and the Info Panel to not display.

## [0.2.0] - Released

### Added
- **Crafting Tree** — visual, pannable, zoomable graph explorer for item relationships. Hover any inventory item and press a configurable hotkey to open.
  - **Bidirectional exploration**: right side shows what an item crafts INTO, left side shows ingredients needed to CREATE it. Right-click nodes to expand/collapse.
  - **Info Panel**: left-click a node to select it and reveal a sidebar showing all non-crafting sources — NPC drops (with percentage and stack range), NPC shop availability, and Shimmer transmutations. Each entry has an icon slot with vanilla hover tooltips.
  - **Animated transitions**: nodes slide in/out with lerp animations on expand/collapse. The info panel slides in from the left edge.
  - **Minimap**: corner overview with bracket-style connection lines matching the main view. Click and drag the minimap to navigate.
  - **Middle-click integration**: middle-click any node while a Terminal is open to jump to that recipe in the crafting tab. Optional auto-minimize toggle (per character).
  - Nodes are color-coded by item category. Cycle detection prevents infinite loops. Draggable, resizable, minimizable window with saved position.
- **In Storage count** — item tooltips now show "In Storage: X" based on the last opened Terminal's network.
- **Debug Tooltips** — client config option. Hold Alt while hovering any item to see its classification, damage type, and internal properties.

### Fixed
- `#` tooltip search now includes dynamic item properties (bait, damage, defense, pickaxe, axe, hammer, accessory, vanity, material, potion, ammo) — e.g. `#bait` now finds fishing bait.
- Bait items now appear under the Ammo filter instead of Consumables.
- Modded boss summoners have an increased likelyhood of being correctly classified as Boss Summoners.
- Modded weapons should now be filtered correctly depending on how they were implemented.

## [0.1.12] - Released

### Added
- **Delta Sync** — server config toggle (`Predictive Sync Mode`). Replaces full disk broadcasts with small item-level change requests. Per-disk sequence numbers with automatic full resync on gap detection. Classic full-sync mode remains as fallback.
- **Recursive Crafting toggle** — new checkbox in the Crafting Tab header. Shows recipes whose ingredients can be crafted from other recipes in storage. Right-click drag to set recursion depth.
- Tooltips on Show Uncraftable and Recursive checkboxes.

### Changed
- Terminal crafting no longer considers player inventory — only storage contents are used for crafting resolution and material consumption.

### Fixed
- **Crafting Tab hitching** — eliminated multiple sources of frame drops in both networking methods
  - Ingredient changes only re-check affected recipes.
  - Filter/sort/search no longer re-scan all disks — uses cached item counts.
  - Station tile hover no longer hitches on first use

## [0.1.11] - Released

### Added
- **Defragment Disks** — new button in the Terminal Disks Tab. Consolidates partially-filled disks by moving items from later disks into earlier ones in Drive Bay order. Fully MP-compatible (server-authoritative).

### Fixed
- Disk panel item grid now renders animated items correctly and shows tooltips on hover.
- Disk Recovery panel now refreshes in real-time when world storage changes — no longer requires closing and reopening the Drive Bay.
- Disk placed in the Disk Recovery slot is now returned if the Recovery window is closed before restoring.

## [0.1.10] - Released

### Added
- **Disk Backups** — storage is automatically backed up as you play. Up to 3 rolling backups are kept per world (current session, previous session, oldest). Backups are written lazily ~10 seconds after a change and flushed on world exit.
- **Backup & Restore UI** — accessible from the client config page. Shows a world dropdown and per-slot timestamps; click Restore to queue a wholesale restore that takes effect on next world load.
- **`tsrestore` server command** — for dedicated server admins. `tsrestore list` shows available backups; `tsrestore <0|1|2>` applies a restore immediately without a world reload and pushes updated state to all connected clients.
- Version number now displayed in both config pages (Server and Client).

### Fixed
- In MP, inserting an unarchived disk into a Drive Bay now correctly restores its items.
- In MP, archiving a disk now broadcasts the GUID removal to all clients — the old GUID no longer lingers in Disk Recovery for other players.
- Disk Recovery (Remap) and Terminal disk upgrade are now server-authoritative in MP; previously both operations ran client-side only and had no effect on the actual world storage.
- Disk Recovery no longer allows duplicating items — recovering a disk now invalidates the original GUID so any surviving copy of the original disk becomes empty.

## [0.1.9] - Released
### Fixed
- Inserting an archived disk into a Drive Bay no longer deletes it — the disk is rejected and stays on the cursor/inventory instead.

## [0.1.8] - Released

### Added
- Placeable Demon Altar and Crimson Altar for Crafting Core use
- Remote Terminal — bind to a Crafting Terminal, open it from anywhere

### Fixed
- Packets sent in MP now use a compact binary format and are sent one disk at a time, staying within Terraria's packet size limit.
- Modded crafting stations appearing as blank slots in the Terminal
- Crafting tab FPS spikes
- Favoriting a recipe no longer affects all variants of that item

### Changed
- Crafting amount field: left-click to type, right-drag to adjust, middle-click to reset
- Crafting conditions are now vanilla items placed in the Crafting Core (Bottomless water/lava/honey buckets, Ice Machine, tombstones), Added crafting recipes for them
- Condition icons shown in crafting panel instead of text tags

### Removed
- Custom source items and tiles (Water Source, Lava Source, Honey Source, Snow Globe, Ectomist Emitter, Shimmer Source)

### TODO
- Terminal does not refresh its connected disk list when another player inserts or removes a disk while it is open. Other players' terminals go stale until reopened. Fix: broadcast an updated disk list to all open terminals on insert/remove, and refresh `_connectedDiskIds` in `TerminalUIState` on receipt.

## [0.1.7] - Prior release

*(No detailed log — see Steam Workshop changelog)*

# Requisition

Adds a networked bulk storage system to Terraria. Store thousands of item stacks across tiered Storage Disks, access and craft from them through a Terminal, and take your collection between worlds.

**This mod is in beta — bugs are expected. Please report anything you find!**

---

## Terminal
The access point for the network. Browse, deposit, withdraw, and craft from a searchable item grid. Draggable and resizable.

**Filtering & Sorting**
16 category filters (left-click to isolate, right-click to toggle). 7 sort modes with ascending/descending toggle. Favorited items always sort to the top and persist per character.

**Search Prefixes**
- Type normally to search by name
- **#** — search by tooltip/property (e.g. **#bait**, **#accessory**)
- **@** — search by mod (e.g. **@calamity**)

**Storage Count Tooltips**
After opening a Terminal, hovering any item anywhere shows how many are in storage.

**Crafting Tab**
- Multi-step crafting — missing intermediate ingredients are crafted automatically
- Toggle to show uncraftable recipes
- Recursive crafting — highlights recipes whose ingredients can also be crafted; right-click drag the checkbox to set recursion depth
- Right-click an ingredient to navigate to its recipe, with full back-navigation history
- Cycle between multiple crafting paths for the same item

## Crafting Core
Place on your network to enable Terminal crafting. Load it with crafting station items — including from other mods — to make them available. Demon and Crimson Altars are provided by default.

## Drive Bay
Holds up to 40 Storage Disks. Multiple Drive Bays link into a single network.

## Storage Disks
Six tiers, each upgradable from the previous in the Terminal (Disks Tab). Only the Basic Disk is craftable. Upgrades preserve the disk's identity and all stored items — nothing is ever lost.

## Disk Archiving
Middle-click a disk to archive it — all stored items are embedded into the disk item itself, removing them from world storage. Carry it to another world and middle-click to unarchive.

## Disk Backups
Up to 3 rolling backups are kept per world, updated continuously. Restore from the **Client Config** page, or use the **tsrestore** console command on dedicated servers (`tsrestore list` / `tsrestore [0|1|2]`).

## Disk Recovery
Opened from the Drive Bay UI. Shows all known disk records including orphaned disks whose physical item was lost. Place a blank replacement disk of matching tier and click Restore to recover its contents.

## Favorited Recipes Panel
A floating panel tracking your favorited recipes. Shows each output item and every ingredient with a live have/need count drawn from storage and inventory. Collapsible and pinnable.

## Crafting Tree
A visual graph explorer for item relationships. Hover any item and press a configurable hotkey to open.

- **Bidirectional** — right side shows what an item crafts into; left side shows its ingredients. Right-click nodes to expand or collapse.
- **Info Panel** — left-click a node to see NPC drop sources (with drop chance and stack range), shop availability, and Shimmer transmutations.
- **Terminal integration** — middle-click any node to jump to that recipe in the crafting tab.
- **Minimap** — corner overview of the full graph; click and drag to navigate.
- Animated transitions, category-colored nodes. Draggable, resizable, minimizable.

## Encyclopedia
A searchable catalog of every item and NPC in the game (vanilla + mods). Press the hotkey (default: **Z**) to open, or hover any item and press the hotkey to jump directly to its entry.

- Browse and filter the full item catalog with the same search prefixes as the Terminal; prefix **!** to switch to NPC browsing
- Item detail panel shows: **Crafted From** (all recipe variants), **Dropped By** (with drop rate and stack range), **Sold By** (with price), **Shimmers Into**, and **Used In** (every recipe that needs it)
- Click any item or NPC in the detail view to navigate to their entry; back-navigation history is preserved
- Middle-click an item to send its recipe directly to the Terminal crafting tab
- Right-click an item to open the Crafting Tree for it

## Multiplayer
Fully multiplayer-compatible. Storage operations are server-authoritative. Optional **Predictive Sync Mode** (server config) uses delta packets instead of full disk broadcasts to reduce network traffic.

## Mod Compatibility
- Accepts any placeable item as a crafting station — no hardcoded list
- Damage class filters catch modded subclasses (Calamity Rogue, True Melee, etc.)
- Disk data is preserved through all storage, cloning, and network operations
- Does not scan the world for tiles — all station availability is driven by items in the Crafting Core

---

## Blocks & Items
- **Drive Bay** — Placeable housing for up to 40 Storage Disks
- **Terminal** — UI access point for browsing, withdrawing, and crafting
- **Remote Terminal** — Binds to a Terminal; opens the full storage UI from anywhere
- **Crafting Core** — Holds station items; enables Terminal crafting
- **Storage Disk T1-T6** — Tiered storage media, upgradeable in sequence

---

## Art
I am not an artist — I have Aphantasia, so it's difficult for me to put my mind on "paper". If you're interested in texturing the items/tiles in this mod, feel free to message me!

---

## Credits
Developed by _/Stixx with assistance from **Claude** (Anthropic).
Claude assisted with architecture design, feature implementation, bug diagnosis, and code review throughout the project. I understand the hate around AI in general, but this simply would not exist without the insight into the very complex details and my limited knowledge of tModLoader API and C#/XNA in general. I write my own code and read every line generated, understanding rather than blindly accepting.

Disk/DriveBay sprites provided by [NonamewouldFit](https://steamcommunity.com/profiles/76561199052428514/).

## Links
- [Change Log](https://steamcommunity.com/sharedfiles/filedetails/changelog/3687137546)
- [Report Bugs](https://steamcommunity.com/sharedfiles/filedetails/discussions/3687137546)
- [UI Review Poll](https://docs.google.com/forms/d/e/1FAIpQLSdJOfgR8zuIduP-bBiLykIMJDc4uW_QsDpNUhRbtH5PAlK1gA/viewform?usp=header)

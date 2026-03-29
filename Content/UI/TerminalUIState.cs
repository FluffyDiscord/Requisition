using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.UI.Elements;
using Terraria.GameInput;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.UI;
using TerraStorage.Content.Tiles;
using TerraStorage.Common;
using TerraStorage.Systems;
using TerraStorage.Content.UI.Elements;
using TerraStorage.Helpers;

namespace TerraStorage.Content.UI
{
    // Controls the order in which consolidated items are displayed in the Terminal's Storage tab.
    public enum SortMode
    {
        ID,
        Name,
        Value,
        DamageDefense,
        Quantity,
        StackCount,
        RecentlyAdded,
        Rarity
    }

    // UIState for the Terminal storage interface. Provides Storage and Crafting tabs,
    // a search bar, category filter, sort bar, item grid, and deposit controls.
    // Handles drag-to-move, corner resize, shift+click deposit, and change-detection
    // via <see cref="StorageWorldSystem.StorageVersion"/> polling.
    public class TerminalUIState : UIState
    {
        private const float PanelMinWidth = 650f;
        private const float PanelMaxWidth = 1200f;
        private const float PanelMinHeight = 300f;

        // Header layout constants
        private const float TabsY = 0f;
        private const float TabsHeight = 25f;
        private const float SearchBarY = 35f;
        private const float SearchBarHeight = 30f;
        private const float FilterBarY = 70f;
        private const float FilterBarHeight = 26f;
        private const float SortBarY = 96f;
        private const float SortBarHeight = 26f;
        private const float ContentY = 122f;
        private const float FooterHeight = 55f;
        // Disks tab has no search/filter/sort bars so content starts higher.
        private const float DisksContentY = 35f;

        private TSWindowElement _mainPanel;
        private StorageSearchBar _searchBar;
        private UICategoryFilterBar _filterBar;
        private UIItemGrid _itemGrid;
        private UICraftingPanel _craftingPanel;
        private UIDiskPanel _diskPanel;
        private UIScrollbar _scrollbar;
        private UIText _statusText;

        private TerminalEntity _terminal;
        private List<Guid> _connectedDiskIds = new();
        private HashSet<int> _availableStations = new();
        private HashSet<CraftingCondition> _availableConditions = new();
        private List<ConsolidatedItem> _cachedItems = new();

        // Tabs
        private enum ActiveTab { Storage, Crafting, Disks }
        private TSTab _storageTab;
        private TSTab _craftingTab;
        private TSTab _disksTab;
        private ActiveTab _activeTab;

        // Sorting
        private SortMode _currentSort = SortMode.ID;
        private bool _sortAscending = true;
        private UISortBar _sortBar;

        // Footer elements (need references for repositioning on resize)
        private TSButton _depositAllBtn;

        // Change detection
        private long _lastStorageVersion = -1;

        private bool _prevMouseLeft;

        // Current panel dimensions
        private float _panelWidth = PanelMinWidth;
        private float _panelHeight;

        // Item property caches for sorting
        private static readonly Dictionary<int, string> _nameCache = new();
        private static readonly Dictionary<int, int> _valueCache = new();
        private static readonly Dictionary<int, int> _damageDefenseCache = new();
        private static readonly Dictionary<int, int> _maxStackCache = new();
        private static readonly Dictionary<int, int> _rarityCache = new();

        // Binds the UI to a specific <see cref="TerminalEntity"/> and immediately
        // refreshes disk connections and the displayed item list.
        public void ClearSearch() => _searchBar?.Clear();

        public bool IsMouseOverPanel() => _mainPanel?.ContainsPoint(Main.MouseScreen) == true;

        public void SetTerminal(TerminalEntity terminal)
        {
            _terminal = terminal;
            RefreshDiskConnections();

            var _dbgPath = TerraStorage.DebugLogPath;
            if (_dbgPath != null)
            {
                try
                {
                    using var fs = new System.IO.FileStream(_dbgPath, System.IO.FileMode.Append,
                        System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                    using var sw = new System.IO.StreamWriter(fs);
                    sw.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}][net={Main.netMode}] SetTerminal: _connectedDiskIds={_connectedDiskIds.Count} [{string.Join(", ", _connectedDiskIds.Select(g => g.ToString()[..8]))}]");
                }
                catch { }
            }

            // In multiplayer, request the latest disk data from the server
            if (Main.netMode == NetmodeID.MultiplayerClient && _connectedDiskIds.Count > 0)
            {
                var mod = ModLoader.GetMod("TerraStorage");
                NetworkHandler.SendRequestDiskData(mod, _connectedDiskIds);
            }

            RefreshItems();
        }

        public override void OnInitialize()
        {
            _panelHeight = Math.Min(Math.Max(400, Main.screenHeight - 340), Main.screenHeight * 0.6f);

            _mainPanel = new TSWindowElement
            {
                StoreKey   = "terminal",
                HasTitleBar = false,
                Resizable  = true,
                WinMinWidth  = PanelMinWidth,
                WinMaxWidth  = PanelMaxWidth,
                WinMinHeight = PanelMinHeight,
                WinMaxHeight = (float)(Main.screenHeight * 0.8f),
            };
            _mainPanel.Width.Set(_panelWidth, 0f);
            _mainPanel.Height.Set(_panelHeight, 0f);
            _mainPanel.Left.Set(14, 0f);
            _mainPanel.Top.Set(310, 0f);
            _mainPanel.LoadSavedBounds();
            _panelWidth  = _mainPanel.Width.Pixels;
            _panelHeight = _mainPanel.Height.Pixels;
            _mainPanel.GetDragZone = mouse =>
            {
                var d = _mainPanel.GetDimensions();
                var id = _mainPanel.GetInnerDimensions();
                // Full strip above the search bar
                if (mouse.Y < d.Y || mouse.Y > id.Y + SearchBarY) return false;
                // In the tab row: exclude where tabs and close button sit
                if (mouse.Y >= id.Y && mouse.Y <= id.Y + TabsHeight + 5)
                {
                    if (mouse.X < id.X + 360) return false; // three tabs span ~0–360px
                    if (mouse.X > id.X + id.Width - 40) return false; // close button
                }
                return true;
            };
            _mainPanel.OnResized += (w, h) =>
            {
                _panelWidth  = w;
                _panelHeight = h;
                RecalculateLayout();
            };
            _mainPanel.SetPadding(12);
            Append(_mainPanel);

            // Close button
            var closeBtn = new TSCloseButton(() => ModContent.GetInstance<TerminalUISystem>().CloseTerminal());
            closeBtn.Left.Set(-26, 1f);
            closeBtn.Top.Set(-4, 0f);
            _mainPanel.Append(closeBtn);

            // Tabs
            _storageTab = new TSTab("Storage");
            _storageTab.Width.Set(105, 0f);
            _storageTab.Height.Set(TabsHeight, 0f);
            _storageTab.Left.Set(10, 0f);
            _storageTab.Top.Set(TabsY, 0f);
            _storageTab.OnLeftClick += (evt, el) => SwitchTab(ActiveTab.Storage);
            _mainPanel.Append(_storageTab);

            _craftingTab = new TSTab("Crafting");
            _craftingTab.Width.Set(105, 0f);
            _craftingTab.Height.Set(TabsHeight, 0f);
            _craftingTab.Left.Set(122, 0f);
            _craftingTab.Top.Set(TabsY, 0f);
            _craftingTab.OnLeftClick += (evt, el) => SwitchTab(ActiveTab.Crafting);
            _mainPanel.Append(_craftingTab);

            _disksTab = new TSTab("Disks");
            _disksTab.Width.Set(105, 0f);
            _disksTab.Height.Set(TabsHeight, 0f);
            _disksTab.Left.Set(234, 0f);
            _disksTab.Top.Set(TabsY, 0f);
            _disksTab.OnLeftClick += (evt, el) => SwitchTab(ActiveTab.Disks);
            _mainPanel.Append(_disksTab);

            // Search bar
            _searchBar = new StorageSearchBar();
            _searchBar.Width.Set(-30, 1f);
            _searchBar.Height.Set(SearchBarHeight, 0f);
            _searchBar.Left.Set(10, 0f);
            _searchBar.Top.Set(SearchBarY, 0f);
            _searchBar.OnTextChanged += OnSearchChanged;
            _mainPanel.Append(_searchBar);

            // Category filter bar
            _filterBar = new UICategoryFilterBar();
            _filterBar.Width.Set(-30, 1f);
            _filterBar.Height.Set(FilterBarHeight, 0f);
            _filterBar.Left.Set(10, 0f);
            _filterBar.Top.Set(FilterBarY, 0f);
            _filterBar.OnFilterChanged += () =>
            {
                if (_activeTab == ActiveTab.Crafting)
                    _craftingPanel.RefreshFilteredRecipes();
                else if (_activeTab == ActiveTab.Storage)
                    FilterAndDisplayItems();
            };
            _mainPanel.Append(_filterBar);

            // Sort bar (below filters) — narrowed on the right to leave room for Deposit All button
            _sortBar = new UISortBar();
            _sortBar.Width.Set(-165, 1f);
            _sortBar.Height.Set(SortBarHeight, 0f);
            _sortBar.Left.Set(10, 0f);
            _sortBar.Top.Set(SortBarY, 0f);
            _sortBar.OnSortChanged += () =>
            {
                _currentSort = (SortMode)_sortBar.Selected;
                _sortAscending = _sortBar.Ascending;
                FilterAndDisplayItems();
                _craftingPanel.SetSortMode(_currentSort, _sortAscending);
            };
            _mainPanel.Append(_sortBar);

            // Content area
            float contentHeight = _panelHeight - ContentY - FooterHeight;

            // Scrollbar
            _scrollbar = new UIScrollbar();
            _scrollbar.Height.Set(contentHeight, 0f);
            _scrollbar.Left.Set(-25, 1f);
            _scrollbar.Top.Set(ContentY, 0f);
            _mainPanel.Append(_scrollbar);

            // Item grid
            _itemGrid = new UIItemGrid();
            _itemGrid.Width.Set(-42, 1f);
            _itemGrid.Height.Set(contentHeight, 0f);
            _itemGrid.Left.Set(10, 0f);
            _itemGrid.Top.Set(ContentY, 0f);
            _itemGrid.SetScrollbar(_scrollbar);
            _itemGrid.OnItemClicked      += OnItemClicked;
            _itemGrid.OnItemRightClicked += OnItemRightClicked;
            _itemGrid.OnItemAltClicked   += OnItemAltClicked;
            _itemGrid.SetFavoriteChecker((type, prefix) =>
                StoragePlayerSystem.Local.IsItemFavorited(type, prefix));
            _mainPanel.Append(_itemGrid);

            // Crafting panel (initially hidden)
            _craftingPanel = new UICraftingPanel();
            _craftingPanel.Width.Set(-20, 1f);
            _craftingPanel.Height.Set(contentHeight, 0f);
            _craftingPanel.Left.Set(10, 0f);
            _craftingPanel.Top.Set(ContentY, 0f);
            // Force child-element creation now so the panel is ready when the tab is first shown.
            _craftingPanel.OnInitialize();
            _craftingPanel.OnFavoritesPanelToggled +=
                () => FavoritedRecipesPanelSystem.Instance.TogglePanel();

            // Disk panel (initially hidden — shown only on Disks tab)
            float disksContentHeight = _panelHeight - DisksContentY - 30f;
            _diskPanel = new UIDiskPanel();
            _diskPanel.Width.Set(-20, 1f);
            _diskPanel.Height.Set(disksContentHeight, 0f);
            _diskPanel.Left.Set(10, 0f);
            _diskPanel.Top.Set(DisksContentY, 0f);

            // Deposit All button — sits at the same Y as the sort bar, right-aligned before the scrollbar
            _depositAllBtn = new TSButton("Deposit All");
            _depositAllBtn.Width.Set(120, 0f);
            _depositAllBtn.Height.Set(SortBarHeight, 0f);
            _depositAllBtn.Left.Set(-145, 1f);
            _depositAllBtn.Top.Set(SortBarY, 0f);
            _depositAllBtn.OnLeftClick += OnDepositAll;
            _mainPanel.Append(_depositAllBtn);

            // Footer status text
            float footerY = _panelHeight - FooterHeight;
            _statusText = new UIText("", 0.8f);
            _statusText.HAlign = 1f;
            _statusText.Left.Set(-8, 0f);
            _statusText.Top.Set(footerY + 18, 0f);
            _mainPanel.Append(_statusText);

            UpdateTabVisuals();
            RecalculateColumns();
        }

        // Switches between the Storage and Crafting tabs, swapping the content-area
        // child elements and refreshing data for the newly active tab.
        private void SwitchTab(ActiveTab tab)
        {
            // Remove content for the current tab.
            switch (_activeTab)
            {
                case ActiveTab.Storage:
                    _mainPanel.RemoveChild(_itemGrid);
                    _mainPanel.RemoveChild(_scrollbar);
                    _mainPanel.RemoveChild(_depositAllBtn);
                    break;
                case ActiveTab.Crafting:
                    _mainPanel.RemoveChild(_craftingPanel);
                    break;
                case ActiveTab.Disks:
                    _mainPanel.RemoveChild(_diskPanel);
                    // Restore the search/filter/sort bars that were hidden for the Disks tab.
                    _mainPanel.Append(_searchBar);
                    _mainPanel.Append(_filterBar);
                    _mainPanel.Append(_sortBar);
                    break;
            }

            _activeTab = tab;
            UpdateTabVisuals();

            // Show content for the new tab.
            switch (_activeTab)
            {
                case ActiveTab.Storage:
                    _mainPanel.Append(_itemGrid);
                    _mainPanel.Append(_scrollbar);
                    _mainPanel.Append(_depositAllBtn);
                    RefreshItems();
                    break;
                case ActiveTab.Crafting:
                    _mainPanel.Append(_craftingPanel);
                    // RefreshDiskConnections pushes disk/station/condition sets to _craftingPanel.
                    RefreshDiskConnections();
                    _craftingPanel.SetCategoryFilter(_filterBar);
                    _craftingPanel.SetSearchText(_searchBar.SearchText);
                    break;
                case ActiveTab.Disks:
                    // Hide search/filter/sort bars — not needed for the Disks tab.
                    _mainPanel.RemoveChild(_searchBar);
                    _mainPanel.RemoveChild(_filterBar);
                    _mainPanel.RemoveChild(_sortBar);
                    _diskPanel.SetTerminal(_terminal);
                    _diskPanel.SetDiskIds(_connectedDiskIds);
                    _diskPanel.SetStations(_availableStations, _availableConditions);
                    _diskPanel.Refresh();
                    _mainPanel.Append(_diskPanel);
                    break;
            }
        }

        private void UpdateTabVisuals()
        {
            bool storageSel  = _activeTab == ActiveTab.Storage;
            bool craftingSel = _activeTab == ActiveTab.Crafting;
            bool disksSel    = _activeTab == ActiveTab.Disks;

            _storageTab.Active  = storageSel;
            _craftingTab.Active = craftingSel;
            _disksTab.Active    = disksSel;

            _storageTab.Top.Set(storageSel  ? TabsY - 3 : TabsY, 0f);
            _storageTab.Height.Set(storageSel  ? TabsHeight + 3 : TabsHeight, 0f);
            _craftingTab.Top.Set(craftingSel ? TabsY - 3 : TabsY, 0f);
            _craftingTab.Height.Set(craftingSel ? TabsHeight + 3 : TabsHeight, 0f);
            _disksTab.Top.Set(disksSel ? TabsY - 3 : TabsY, 0f);
            _disksTab.Height.Set(disksSel ? TabsHeight + 3 : TabsHeight, 0f);

            _storageTab.Recalculate();
            _craftingTab.Recalculate();
            _disksTab.Recalculate();
        }

        private void RefreshDiskConnections()
        {
            if (_terminal != null)
            {
                _connectedDiskIds = _terminal.GetConnectedDiskIds();
                var (stations, conditions) = _terminal.GetStationsAndConditions();
                _availableStations = stations;
                _availableConditions = conditions;
            }
            else
            {
                _connectedDiskIds.Clear();
                _availableStations.Clear();
                _availableConditions.Clear();
            }

            if (_activeTab == ActiveTab.Crafting)
            {
                _craftingPanel.SetAvailableStations(_availableStations);
                _craftingPanel.SetConditions(_availableConditions);
                _craftingPanel.SetDiskIds(_connectedDiskIds);
            }
            else if (_activeTab == ActiveTab.Disks)
            {
                _diskPanel.SetDiskIds(_connectedDiskIds);
                _diskPanel.SetStations(_availableStations, _availableConditions);
                _diskPanel.Refresh();
            }

            FavoritedRecipesPanelSystem.Instance?.SetDiskIds(_connectedDiskIds);
            StoragePlayerSystem.Local.SetLastOpenedDiskIds(_connectedDiskIds);
        }

        private (int used, int max) GetStorageCapacity()
        {
            int used = 0, max = 0;
            foreach (var id in _connectedDiskIds)
            {
                var disk = StorageWorldSystem.Instance.GetDiskData(id);
                if (disk != null)
                {
                    used += disk.UsedStacks;
                    max += disk.MaxStacks;
                }
            }
            return (used, max);
        }

        private void RefreshItems()
        {
            if (_connectedDiskIds.Count == 0)
            {
                _cachedItems.Clear();
                _itemGrid?.SetItems(new List<ConsolidatedItem>());
                _statusText?.SetText(Language.GetText("Mods.TerraStorage.UI.DiskPanel.SlotCount").Format(0, 0));
                return;
            }

            _cachedItems = StorageWorldSystem.Instance.GetConsolidatedItems(_connectedDiskIds);
            FilterAndDisplayItems();
        }

        // Applies the current search text, category filter, and sort mode to
        // <see cref="_cachedItems"/>, then pushes the result to <see cref="_itemGrid"/>.
        // Favorited items are partitioned into a separate list that is sorted
        // independently and always prepended before filtered results.
        private void FilterAndDisplayItems()
        {
            var player = StoragePlayerSystem.Local;
            var search = _searchBar?.SearchText ?? "";
            bool hasSearch = !string.IsNullOrEmpty(search);
            bool hasFilter = _filterBar != null;

            var favorited = new List<ConsolidatedItem>();
            var regular = new List<ConsolidatedItem>();

            foreach (var ci in _cachedItems)
            {
                if (player.IsItemFavorited(ci.ItemType, ci.PrefixId))
                {
                    favorited.Add(ci);
                }
                else
                {
                    if (hasSearch && !ItemSearchHelper.Matches(ci.ItemType, search))
                        continue;
                    if (hasFilter && !_filterBar.PassesFilter(ci.ItemType))
                        continue;
                    regular.Add(ci);
                }
            }

            SortItems(favorited);
            SortItems(regular);

            favorited.AddRange(regular);
            _itemGrid?.SetItems(favorited);
            var (used, max) = GetStorageCapacity();
            _statusText?.SetText(Language.GetText("Mods.TerraStorage.UI.DiskPanel.SlotCount").Format(used, max));
        }

        // Sorts <paramref name="items"/> in-place according to <see cref="_currentSort"/>
        // and <see cref="_sortAscending"/>. Item property lookups are cached to avoid
        // repeated <c>SetDefaults</c> allocations during multi-item comparisons. 
        private void SortItems(List<ConsolidatedItem> items)
        {
            // Flip the comparison result for descending order instead of reversing afterwards.
            int dir = _sortAscending ? 1 : -1;
            items.Sort((a, b) =>
            {
                int result = _currentSort switch
                {
                    SortMode.ID => a.ItemType.CompareTo(b.ItemType),
                    SortMode.Name => string.Compare(GetCachedName(a.ItemType), GetCachedName(b.ItemType), StringComparison.OrdinalIgnoreCase),
                    SortMode.Value => GetCachedValue(a.ItemType).CompareTo(GetCachedValue(b.ItemType)),
                    SortMode.DamageDefense => GetCachedDamageDefense(a.ItemType).CompareTo(GetCachedDamageDefense(b.ItemType)),
                    SortMode.Quantity => a.TotalCount.CompareTo(b.TotalCount),
                    SortMode.StackCount => GetCachedMaxStack(a.ItemType).CompareTo(GetCachedMaxStack(b.ItemType)),
                    SortMode.RecentlyAdded => a.LatestInsertionOrder.CompareTo(b.LatestInsertionOrder),
                    SortMode.Rarity => GetCachedRarity(a.ItemType).CompareTo(GetCachedRarity(b.ItemType)),
                    _ => a.ItemType.CompareTo(b.ItemType)
                };
                return result * dir;
            });
        }

        internal static string GetCachedName(int itemType)
        {
            if (!_nameCache.TryGetValue(itemType, out var name))
            {
                var item = new Item();
                item.SetDefaults(itemType);
                name = item.Name;
                _nameCache[itemType] = name;
            }
            return name;
        }

        internal static int GetCachedValue(int itemType)
        {
            if (!_valueCache.TryGetValue(itemType, out var value))
            {
                var item = new Item();
                item.SetDefaults(itemType);
                value = item.value;
                _valueCache[itemType] = value;
            }
            return value;
        }

        internal static int GetCachedDamageDefense(int itemType)
        {
            if (!_damageDefenseCache.TryGetValue(itemType, out var val))
            {
                var item = new Item();
                item.SetDefaults(itemType);
                // Prefer damage for weapons; fall back to defense for armor/accessories.
                val = item.damage > 0 ? item.damage : item.defense;
                _damageDefenseCache[itemType] = val;
            }
            return val;
        }

        private static int GetCachedMaxStack(int itemType)
        {
            if (!_maxStackCache.TryGetValue(itemType, out var val))
            {
                var item = new Item();
                item.SetDefaults(itemType);
                val = item.maxStack;
                _maxStackCache[itemType] = val;
            }
            return val;
        }

        internal static int GetCachedRarity(int itemType)
        {
            if (!_rarityCache.TryGetValue(itemType, out var val))
            {
                var item = new Item();
                item.SetDefaults(itemType);
                val = item.rare;
                _rarityCache[itemType] = val;
            }
            return val;
        }

        private void OnSearchChanged(string text)
        {
            if (_activeTab == ActiveTab.Crafting)
                _craftingPanel.SetSearchText(text);
            else if (_activeTab == ActiveTab.Storage)
                FilterAndDisplayItems();
        }

        private void OnItemAltClicked(ConsolidatedItem item)
        {
            StoragePlayerSystem.Local.ToggleItemFavorite(item.ItemType, item.PrefixId);
            SoundEngine.PlaySound(SoundID.MenuTick);
            FilterAndDisplayItems();
        }

        // Handles a left-click on a storage item. If the player is already holding an item
        // the cursor item is deposited instead. Otherwise extracts up to one max-stack worth
        // of the clicked item; shift+click sends it directly to the player's inventory.
        // Any overflow that doesn't fit in the inventory is re-inserted into storage. 
        private void OnItemClicked(ConsolidatedItem item)
        {
            if (item == null || _connectedDiskIds.Count == 0)
                return;

            bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);

            if (Main.mouseItem != null && !Main.mouseItem.IsAir)
            {
                DepositCursorItem();
                return;
            }

            Item extracted;
            if (item.ModData != null)
            {
                // Per-instance item (UnloadedItem, unique NBT): extract the exact stack the user clicked.
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    var mod = ModLoader.GetMod("TerraStorage");
                    NetworkHandler.SendWithdrawItemByModData(mod, _connectedDiskIds, item.ModData, shift);
                    SoundEngine.PlaySound(SoundID.Grab);
                    return;
                }
                extracted = StorageWorldSystem.Instance.ExtractItemWithModData(_connectedDiskIds, item.ModData);
            }
            else if (item.FullItemTag != null)
            {
                // Per-instance item with GlobalItem data only (e.g. Entropy enchantments).
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    var mod = ModLoader.GetMod("TerraStorage");
                    NetworkHandler.SendWithdrawItemByFullItemTag(mod, _connectedDiskIds, item.FullItemTag, shift);
                    SoundEngine.PlaySound(SoundID.Grab);
                    return;
                }
                extracted = StorageWorldSystem.Instance.ExtractItemWithFullItemTag(_connectedDiskIds, item.FullItemTag);
            }
            else
            {
                var tempItem = new Item();
                tempItem.SetDefaults(item.ItemType);
                int withdrawCount = Math.Min(tempItem.maxStack, item.TotalCount);

                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    var mod = ModLoader.GetMod("TerraStorage");
                    NetworkHandler.SendWithdrawItem(mod, _connectedDiskIds, item.ItemType, withdrawCount, item.PrefixId, shift);
                    SoundEngine.PlaySound(SoundID.Grab);
                    return;
                }
                extracted = StorageWorldSystem.Instance.ExtractItem(
                    _connectedDiskIds, item.ItemType, withdrawCount, item.PrefixId);
            }

            if (!extracted.IsAir)
            {
                if (shift)
                {
                    var player = Main.LocalPlayer;
                    // Directly place item in inventory instead of using ground pickup logic
                    bool placedInInventory = false;
                    for (int i = 0; i < 50; i++)
                    {
                        if (player.inventory[i].IsAir)
                        {
                            player.inventory[i] = extracted.Clone();
                            placedInInventory = true;
                            break;
                        }
                        else if (player.inventory[i].type == extracted.type && 
                                 player.inventory[i].prefix == extracted.prefix &&
                                 player.inventory[i].stack < player.inventory[i].maxStack)
                        {
                            int spaceAvailable = player.inventory[i].maxStack - player.inventory[i].stack;
                            if (spaceAvailable >= extracted.stack)
                            {
                                player.inventory[i].stack += extracted.stack;
                                placedInInventory = true;
                                break;
                            }
                            else
                            {
                                // Partially fill the stack and put remaining in next empty slot
                                extracted.stack -= spaceAvailable;
                                player.inventory[i].stack = player.inventory[i].maxStack;
                            }
                        }
                    }
                    
                    if (!placedInInventory)
                    {
                        // If no inventory space, re-insert into storage
                        StorageWorldSystem.Instance.InsertItem(_connectedDiskIds, extracted);
                    }
                }
                else
                {
                    if (Main.mouseItem.IsAir)
                        Main.mouseItem = extracted;
                    else
                        StorageWorldSystem.Instance.InsertItem(_connectedDiskIds, extracted);
                }
                SoundEngine.PlaySound(SoundID.Grab);
            }

            RefreshItems();
        }

        // Handles a right-click on a storage item, extracting exactly one unit.
        // If the cursor already holds a matching item and has room the extracted unit
        // is merged; otherwise it is re-inserted so nothing is lost.
        private void OnItemRightClicked(ConsolidatedItem item)
        {
            if (item == null || _connectedDiskIds.Count == 0)
                return;

            Item extracted;
            if (item.ModData != null)
            {
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    var mod = ModLoader.GetMod("TerraStorage");
                    NetworkHandler.SendWithdrawItemByModData(mod, _connectedDiskIds, item.ModData);
                    SoundEngine.PlaySound(SoundID.Grab);
                    return;
                }
                extracted = StorageWorldSystem.Instance.ExtractItemWithModData(_connectedDiskIds, item.ModData);
            }
            else if (item.FullItemTag != null)
            {
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    var mod = ModLoader.GetMod("TerraStorage");
                    NetworkHandler.SendWithdrawItemByFullItemTag(mod, _connectedDiskIds, item.FullItemTag);
                    SoundEngine.PlaySound(SoundID.Grab);
                    return;
                }
                extracted = StorageWorldSystem.Instance.ExtractItemWithFullItemTag(_connectedDiskIds, item.FullItemTag);
            }
            else
            {
                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    var mod = ModLoader.GetMod("TerraStorage");
                    NetworkHandler.SendWithdrawItem(mod, _connectedDiskIds, item.ItemType, 1, item.PrefixId);
                    SoundEngine.PlaySound(SoundID.Grab);
                    return;
                }
                extracted = StorageWorldSystem.Instance.ExtractItem(
                    _connectedDiskIds, item.ItemType, 1, item.PrefixId);
            }

            if (!extracted.IsAir)
            {
                if (Main.mouseItem.IsAir)
                {
                    Main.mouseItem = extracted;
                }
                else if (Main.mouseItem.type == extracted.type && Main.mouseItem.prefix == extracted.prefix
                    && Main.mouseItem.stack < Main.mouseItem.maxStack)
                {
                    Main.mouseItem.stack += extracted.stack;
                }
                else
                {
                    StorageWorldSystem.Instance.InsertItem(_connectedDiskIds, extracted);
                }
                SoundEngine.PlaySound(SoundID.Grab);
            }

            RefreshItems();
        }

        private void DepositCursorItem()
        {
            if (Main.mouseItem == null || Main.mouseItem.IsAir || _connectedDiskIds.Count == 0)
                return;

            if (Main.netMode == NetmodeID.MultiplayerClient)
            {
                var mod = ModLoader.GetMod("TerraStorage");
                NetworkHandler.SendDepositItem(mod, _connectedDiskIds, Main.mouseItem);
                Main.mouseItem.TurnToAir();
                SoundEngine.PlaySound(SoundID.Grab);
                return;
            }

            int leftover = StorageWorldSystem.Instance.InsertItem(_connectedDiskIds, Main.mouseItem);
            if (leftover <= 0)
                Main.mouseItem.TurnToAir();
            else
                Main.mouseItem.stack = leftover;

            SoundEngine.PlaySound(SoundID.Grab);
            RefreshItems();
        }

        // Deposits the non-favorited main inventory items (slots 10–49) into storage.
        // Hotbar slots (0–9) and favorited items are intentionally skipped.
        private void OnDepositAll(UIMouseEvent evt, UIElement element)
        {
            if (_connectedDiskIds.Count == 0)
                return;

            var player = Main.LocalPlayer;
            var mod = Main.netMode == NetmodeID.MultiplayerClient
                ? ModLoader.GetMod("TerraStorage")
                : null;

            for (int i = 10; i < 50; i++)
            {
                if (player.inventory[i].IsAir || player.inventory[i].favorited)
                    continue;

                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    NetworkHandler.SendDepositItem(mod, _connectedDiskIds, player.inventory[i]);
                    player.inventory[i].TurnToAir();
                }
                else
                {
                    int leftover = StorageWorldSystem.Instance.InsertItem(_connectedDiskIds, player.inventory[i]);
                    if (leftover <= 0)
                        player.inventory[i].TurnToAir();
                    else
                        player.inventory[i].stack = leftover;
                }
            }

            SoundEngine.PlaySound(SoundID.Grab);
            RefreshItems();
        }

        public override void Update(GameTime gameTime)
        {
            // Pre-detect shift+click on an inventory slot BEFORE base.Update() propagates
            // LeftClick events to child elements. When the terminal panel is positioned near
            // the inventory (e.g. after dragging), the crafting panel's LeftClick handler
            // could fire ExecuteCraft() instead of depositing the clicked item. Consuming
            // the click blocker here prevents that from happening.
            bool shift = Main.keyState.IsKeyDown(Keys.LeftShift) || Main.keyState.IsKeyDown(Keys.RightShift);
            bool shiftInventoryDeposit = false;
            if (Main.mouseLeft && !_prevMouseLeft && !UIClickBlocker.IsConsumed && shift)
            {
                for (int i = 0; i < 50; i++)
                {
                    if (!Main.LocalPlayer.inventory[i].IsAir && DriveBayUIState.IsMouseOverInventorySlot(i))
                    {
                        UIClickBlocker.Consume();
                        shiftInventoryDeposit = true;
                        break;
                    }
                }
            }

            base.Update(gameTime);

            // If the Crafting Tree selected a recipe, switch to the Crafting tab
            if (CraftingTree.CraftingTreeState.PendingRecipeSelection != null && _activeTab != ActiveTab.Crafting)
                SwitchTab(ActiveTab.Crafting);

            if (_mainPanel.ContainsPoint(Main.MouseScreen))
            {
                Main.LocalPlayer.mouseInterface = true;
                PlayerInput.LockVanillaMouseScroll("TerraStorage");
            }

            // Block Terraria's inventory handling when shift is held so we can intercept shift+click
            if (shift && !_mainPanel.ContainsPoint(Main.MouseScreen))
            {
                for (int i = 0; i < 50; i++)
                {
                    if (DriveBayUIState.IsMouseOverInventorySlot(i))
                    {
                        Main.LocalPlayer.mouseInterface = true;
                        break;
                    }
                }
            }

            bool justClicked = Main.mouseLeft && !UIClickBlocker.IsConsumed;

            if (justClicked)
            {
                if (_mainPanel.ContainsPoint(Main.MouseScreen))
                {
                    UIClickBlocker.Consume();

                    if (_activeTab == ActiveTab.Storage && Main.mouseItem != null && !Main.mouseItem.IsAir
                        && !_itemGrid.ContainsPoint(Main.MouseScreen))
                    {
                        DepositCursorItem();
                    }
                }
                else if (shift)
                {
                    HandleInventoryShiftClick();
                }
            }
            else if (shiftInventoryDeposit)
            {
                // The pre-check consumed the click blocker to prevent panel overlap from
                // accidentally triggering ExecuteCraft(). Perform the deposit now.
                HandleInventoryShiftClick();
            }

            // Refresh disk connections every ~2 seconds; topology changes are infrequent.
            if (Main.GameUpdateCount % 120 == 0)
            {
                RefreshDiskConnections();
                if (_activeTab == ActiveTab.Storage)
                    RefreshItems();
            }

            // Poll StorageVersion so the UI updates whenever items are inserted/extracted
            // by any code path (multiplayer sync, crafting, etc.) without needing callbacks.
            // NOTE: do NOT call RefreshDiskConnections() here — that scans TileEntity.ByID
            // twice and is expensive. Topology changes (disk inserted/removed) are handled
            // by the 2-second periodic refresh above. This handler only deals with contents.
            long currentVersion = StorageWorldSystem.Instance.StorageVersion;
            if (currentVersion != _lastStorageVersion)
            {
                _lastStorageVersion = currentVersion;
                switch (_activeTab)
                {
                    case ActiveTab.Crafting:
                        // Disk IDs are unchanged — let the crafting panel's own throttled
                        // refresh detect the version change and update at its next tick.
                        var (used, max) = GetStorageCapacity();
                        _statusText?.SetText(Language.GetText("Mods.TerraStorage.UI.DiskPanel.SlotCount").Format(used, max));
                        break;
                    case ActiveTab.Storage:
                        RefreshItems();
                        break;
                    case ActiveTab.Disks:
                        // DrawSelf reads live DiskData — no explicit refresh needed.
                        break;
                }
            }

            _prevMouseLeft = Main.mouseLeft;
        }

        private void RecalculateLayout()
        {
            float contentHeight = _panelHeight - ContentY - FooterHeight;
            float footerY = _panelHeight - FooterHeight;

            _scrollbar.Height.Set(contentHeight, 0f);
            _itemGrid.Height.Set(contentHeight, 0f);
            _craftingPanel.Height.Set(contentHeight, 0f);
            _diskPanel.Height.Set(_panelHeight - DisksContentY - 30f, 0f);
            _statusText.Top.Set(footerY + 18, 0f);

            RecalculateColumns();
        }

        // Recomputes the number of item-grid columns that fit in the current panel width.
        // Each slot is 48 px wide; 66 px is reserved for panel padding, scrollbar, and margins.
        private void RecalculateColumns()
        {
            float gridWidth = _panelWidth - 66; // padding + scrollbar + margins
            int columns = Math.Max(1, (int)(gridWidth / 48));
            _itemGrid.SetColumns(columns);
        }

        protected override void DrawSelf(SpriteBatch spriteBatch)
        {
            base.DrawSelf(spriteBatch);
        }

        private void HandleInventoryShiftClick()
        {
            if (_connectedDiskIds.Count == 0)
                return;

            var player = Main.LocalPlayer;
            for (int i = 0; i < 50; i++)
            {
                if (player.inventory[i].IsAir)
                    continue;

                if (!DriveBayUIState.IsMouseOverInventorySlot(i))
                    continue;

                if (Main.netMode == NetmodeID.MultiplayerClient)
                {
                    var mod = ModLoader.GetMod("TerraStorage");
                    NetworkHandler.SendDepositItem(mod, _connectedDiskIds, player.inventory[i]);
                    player.inventory[i].TurnToAir();
                }
                else
                {
                    int leftover = StorageWorldSystem.Instance.InsertItem(_connectedDiskIds, player.inventory[i]);
                    if (leftover <= 0)
                        player.inventory[i].TurnToAir();
                    else
                        player.inventory[i].stack = leftover;
                }

                SoundEngine.PlaySound(SoundID.Grab);
                RefreshItems();
                return;
            }
        }
    }
}

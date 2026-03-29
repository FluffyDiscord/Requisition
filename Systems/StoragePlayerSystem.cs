using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TerraStorage.Content.Items;

namespace TerraStorage.Systems
{
    // Per-player mod data that persists favorited items and recipes across sessions.
    // Favorites affect how items are sorted and displayed in the Terminal UI
    // (always appear first regardless of other sort settings). 
    public class StoragePlayerSystem : ModPlayer
    {
        // (itemType, prefixId) tuple used as key so the same item with different modifiers
        // can be favorited independently
        private readonly HashSet<(int type, int prefix)> _favoritedItems = new();
        private readonly HashSet<int> _favoritedRecipes = new();
        private bool _starterGiven;

        // Reverse lookup: Recipe reference → recipe index. Built lazily, avoids O(n)
        // Array.IndexOf scans in IsRecipeFavorited/ToggleRecipeFavorite.
        private static Dictionary<Recipe, int> _recipeIndexLookup;

        private static Dictionary<Recipe, int> RecipeIndexLookup
        {
            get
            {
                if (_recipeIndexLookup == null || _recipeIndexLookup.Count == 0)
                {
                    _recipeIndexLookup = new Dictionary<Recipe, int>(Recipe.numRecipes);
                    for (int i = 0; i < Recipe.numRecipes; i++)
                    {
                        if (Main.recipe[i] != null)
                            _recipeIndexLookup[Main.recipe[i]] = i;
                    }
                }
                return _recipeIndexLookup;
            }
        }

        // Last-opened terminal's disk IDs — used by tooltip overlay to show storage counts.
        // Session-only, not persisted.
        private List<Guid> _lastOpenedDiskIds = new();

        public IReadOnlyList<Guid> LastOpenedDiskIds => _lastOpenedDiskIds;

        public void SetLastOpenedDiskIds(List<Guid> diskIds)
        {
            _lastOpenedDiskIds = diskIds ?? new List<Guid>();
        }

        public static StoragePlayerSystem Local => Main.LocalPlayer.GetModPlayer<StoragePlayerSystem>();

        public bool IsItemFavorited(int type, int prefix) => _favoritedItems.Contains((type, prefix));

        //Returns true if this specific recipe variant is favorited.
        public bool IsRecipeFavorited(Recipe recipe)
        {
            if (_favoritedRecipes.Count == 0) return false;
            return RecipeIndexLookup.TryGetValue(recipe, out int idx) && _favoritedRecipes.Contains(idx);
        }

        public IReadOnlyCollection<int> FavoritedRecipes => _favoritedRecipes;

        // Toggles the favorite state of an item. Uses a remove-first pattern:
        // if the key was already present it gets removed, otherwise it is added.
        public void ToggleItemFavorite(int type, int prefix)
        {
            var key = (type, prefix);
            if (!_favoritedItems.Remove(key))
                _favoritedItems.Add(key);
        }

        //Toggles the favorite state of this specific recipe variant.
        public void ToggleRecipeFavorite(Recipe recipe)
        {
            if (!RecipeIndexLookup.TryGetValue(recipe, out int idx)) return;
            if (!_favoritedRecipes.Remove(idx))
                _favoritedRecipes.Add(idx);
        }

        public override void OnEnterWorld()
        {
            if (_starterGiven || !TerraStorageConfig.Instance.QuickStarterPack)
                return;

            _starterGiven = true;

            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<TerminalItem>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<CraftingCoreItem>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<DriveBayItem>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<StorageDiskTier2>());
        }

        //Whether the Crafting Tree auto-minimizes on middle-click recipe selection.
        public bool CraftingTreeAutoMinimize { get; set; } = true;

        public override void SaveData(TagCompound tag)
        {
            // Serialize each favorited item as a small TagCompound with "t" (type) and "p" (prefix)
            tag["favItems"] = _favoritedItems
                .Select(f => new TagCompound { ["t"] = f.type, ["p"] = f.prefix })
                .ToList();
            tag["favRecipeIdx"] = _favoritedRecipes.ToList();
            tag["starterGiven"] = _starterGiven;
            tag["craftingTreeAutoMin"] = CraftingTreeAutoMinimize;
        }

        public override void LoadData(TagCompound tag)
        {
            _favoritedItems.Clear();
            _favoritedRecipes.Clear();

            if (tag.ContainsKey("favItems"))
                foreach (var e in tag.GetList<TagCompound>("favItems"))
                    _favoritedItems.Add((e.GetInt("t"), e.GetInt("p")));

            if (tag.ContainsKey("favRecipeIdx"))
                foreach (int recipeIdx in tag.GetList<int>("favRecipeIdx"))
                    _favoritedRecipes.Add(recipeIdx);

            _starterGiven = tag.GetBool("starterGiven");
            CraftingTreeAutoMinimize = !tag.ContainsKey("craftingTreeAutoMin") || tag.GetBool("craftingTreeAutoMin");
        }
    }
}

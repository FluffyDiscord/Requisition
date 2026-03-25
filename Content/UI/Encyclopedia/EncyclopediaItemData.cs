using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using TerraStorage.Content.UI.CraftingTree;
using TerraStorage.Content.UI.Elements;
using TerraStorage.Systems;

namespace TerraStorage.Content.UI.Encyclopedia
{
    /// <summary>
    /// ModSystem that pre-builds all encyclopedia data during PostSetupRecipes
    /// so there is no lazy-init stall during gameplay.
    /// </summary>
    public class EncyclopediaItemData : ModSystem
    {
        public struct RecipeInfo
        {
            public int ResultType;
            public int ResultStack;
            public int RecipeIndex; // index into Main.recipe[]
            public (int type, int stack)[] Ingredients;
            public int[] RequiredTiles;
            public string[] ConditionDescriptions;
        }

        /// <summary>Pre-built list of all valid items, ready to use on first open.</summary>
        public static List<ConsolidatedItem> AllItems { get; private set; }

        private static Dictionary<int, List<RecipeInfo>> _recipesFor;
        private static Dictionary<int, List<RecipeInfo>> _usagesFor;
        private static readonly List<RecipeInfo> _emptyRecipes = new();

        public static List<RecipeInfo> GetRecipesFor(int itemType)
            => _recipesFor != null && _recipesFor.TryGetValue(itemType, out var list) ? list : _emptyRecipes;

        public static List<RecipeInfo> GetUsagesFor(int itemType)
            => _usagesFor != null && _usagesFor.TryGetValue(itemType, out var list) ? list : _emptyRecipes;

        public override void PostSetupRecipes()
        {
            if (Main.dedServ) return;
            BuildItemCatalog();
            BuildRecipeCaches();
        }

        public override void OnWorldLoad()
        {
            if (Main.dedServ || AllItems == null) return;
            WarmClientCaches();
        }

        /// <summary>
        /// Pre-populates the lazy per-item caches (category classification, name lookup)
        /// so the first Encyclopedia open is instant. Runs during world load; cache hits
        /// on every subsequent entry are O(1).
        /// </summary>
        private static void WarmClientCaches()
        {
            foreach (var ci in AllItems)
            {
                UICategoryFilterBar.ClassifyItem(ci.ItemType);
                ItemSearchHelper.GetName(ci.ItemType);
            }
        }

        public override void Unload()
        {
            AllItems = null;
            _recipesFor = null;
            _usagesFor = null;
        }

        /// <summary>
        /// Sends the first recipe for itemType to the crafting terminal.
        /// No-op if the item has no recipes or the index is invalid.
        /// </summary>
        public static void TrySendRecipeToTerminal(int itemType)
        {
            var recipes = GetRecipesFor(itemType);
            if (recipes.Count == 0) return;
            int idx = recipes[0].RecipeIndex;
            if (idx >= 0 && idx < Recipe.numRecipes)
                CraftingTreeState.PendingRecipeSelection = Main.recipe[idx];
        }

        private static void BuildItemCatalog()
        {
            AllItems = new List<ConsolidatedItem>();
            int count = ItemLoader.ItemCount;
            for (int i = 1; i < count; i++)
            {
                // Lang lookup is far cheaper than SetDefaults for every item
                string name = Lang.GetItemNameValue(i);
                if (string.IsNullOrWhiteSpace(name)) continue;

                AllItems.Add(new ConsolidatedItem
                {
                    ItemType = i,
                    PrefixId = 0,
                    TotalCount = 0
                });
            }
        }

        private static void BuildRecipeCaches()
        {
            _recipesFor = new Dictionary<int, List<RecipeInfo>>();
            _usagesFor  = new Dictionary<int, List<RecipeInfo>>();

            for (int r = 0; r < Recipe.numRecipes; r++)
            {
                var recipe = Main.recipe[r];
                if (recipe.createItem == null || recipe.createItem.type <= ItemID.None)
                    continue;

                var ingredients = new List<(int type, int stack)>();
                foreach (var req in recipe.requiredItem)
                {
                    if (req.type > ItemID.None)
                        ingredients.Add((req.type, req.stack));
                }

                var tiles = new List<int>();
                foreach (int t in recipe.requiredTile)
                {
                    if (t >= 0) tiles.Add(t);
                }

                var conditions = new List<string>();
                if (recipe.Conditions != null)
                {
                    foreach (var cond in recipe.Conditions)
                        conditions.Add(cond.Description.Value);
                }

                var info = new RecipeInfo
                {
                    ResultType            = recipe.createItem.type,
                    ResultStack           = recipe.createItem.stack,
                    RecipeIndex           = r,
                    Ingredients           = ingredients.ToArray(),
                    RequiredTiles         = tiles.ToArray(),
                    ConditionDescriptions = conditions.ToArray()
                };

                if (!_recipesFor.TryGetValue(info.ResultType, out var rList))
                {
                    rList = new List<RecipeInfo>();
                    _recipesFor[info.ResultType] = rList;
                }
                rList.Add(info);

                foreach (var (type, _) in info.Ingredients)
                {
                    if (!_usagesFor.TryGetValue(type, out var uList))
                    {
                        uList = new List<RecipeInfo>();
                        _usagesFor[type] = uList;
                    }
                    uList.Add(info);
                }
            }
        }
    }
}

using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Requisition.Content.UI.CraftingTree;
using Requisition.Content.UI.Elements;
using Requisition.Systems;

namespace Requisition.Content.UI.Encyclopedia
{
        // ModSystem that pre-builds all encyclopedia data during PostSetupRecipes
    // so there is no lazy-init stall during gameplay.
    // 
    public class EncyclopediaItemData : ModSystem
    {
        public struct RecipeInfo
        {
            public int ResultType;
            public int ResultStack;
            public int RecipeIndex; // index into Main.recipe[]
            public (int type, int stack)[] Ingredients;
            public int?[] IngredientGroupIds; // parallel to Ingredients; null = no group
            public int[] RequiredTiles;
            public string[] ConditionDescriptions;
        }

        //Pre-built list of all valid items, ready to use on first open.
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

                // Pre-populates the lazy per-item caches (category classification, name lookup)
        // so the first Encyclopedia open is instant. Runs during world load; cache hits
        // on every subsequent entry are O(1).
        // 
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

                // Sends the first recipe for itemType to the crafting terminal.
        // No-op if the item has no recipes or the index is invalid.
        // 
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
                var groupIds = new List<int?>();
                foreach (var req in recipe.requiredItem)
                {
                    if (req.type > ItemID.None)
                    {
                        ingredients.Add((req.type, req.stack));
                        int? gid = null;
                        foreach (int g in recipe.acceptedGroups)
                        {
                            if (RecipeGroup.recipeGroups[g].ContainsItem(req.type)) { gid = g; break; }
                        }
                        groupIds.Add(gid);
                    }
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
                    IngredientGroupIds    = groupIds.ToArray(),
                    RequiredTiles         = tiles.ToArray(),
                    ConditionDescriptions = conditions.ToArray()
                };

                if (!_recipesFor.TryGetValue(info.ResultType, out var rList))
                {
                    rList = new List<RecipeInfo>();
                    _recipesFor[info.ResultType] = rList;
                }
                rList.Add(info);

                for (int i = 0; i < info.Ingredients.Length; i++)
                {
                    var ingType = info.Ingredients[i].type;
                    if (!_usagesFor.TryGetValue(ingType, out var uList))
                    {
                        uList = new List<RecipeInfo>();
                        _usagesFor[ingType] = uList;
                    }
                    uList.Add(info);

                    // Also register all group substitutes so "Used In" fires for e.g. LeadBar
                    if (info.IngredientGroupIds[i].HasValue)
                    {
                        var grp = RecipeGroup.recipeGroups[info.IngredientGroupIds[i].Value];
                        foreach (int sub in grp.ValidItems)
                        {
                            if (sub == ingType) continue;
                            if (!_usagesFor.TryGetValue(sub, out var subList))
                            {
                                subList = new List<RecipeInfo>();
                                _usagesFor[sub] = subList;
                            }
                            subList.Add(info);
                        }
                    }
                }
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using TerraStorage.Common;
using TerraStorage.Systems;

namespace TerraStorage.Helpers.Resolver
{
    // Adapts the live Terraria recipe world (Main.recipe, RecipeGroup, RecipeCacheSystem, station
    // adjacency, recipe conditions) to <see cref="IRecipeEnvironment"/> so the pure
    // <see cref="CoreResolver"/> drives every recursive decision. Station and condition availability
    // vary per request and are held per instance; the recipe->CoreRecipe conversion is session-stable
    // and cached statically so building an environment per call is cheap.
    public sealed class TerrariaRecipeEnvironment : IRecipeEnvironment
    {
        private readonly HashSet<int> _stations;
        private readonly HashSet<CraftingCondition> _conditions;

        private static readonly Dictionary<Recipe, CoreRecipe> _coreByRecipe = new();
        private static readonly Dictionary<int, List<CoreRecipe>> _producingCache = new();
        private static readonly Dictionary<int, List<int>> _groupItemsCache = new();
        private static List<CoreRecipe> _allCore;
        private static int _allCoreBuiltFor = -1;

        public TerrariaRecipeEnvironment(HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions)
        {
            _stations = availableStations;
            _conditions = availableConditions ?? new HashSet<CraftingCondition>();
        }

        // Converts a Terraria recipe to its CoreRecipe form (cached). Drops air ingredients and the
        // -1 station sentinels so the core never has to re-filter them.
        public static CoreRecipe ToCore(Recipe recipe)
        {
            if (_coreByRecipe.TryGetValue(recipe, out var cached))
                return cached;

            var core = new CoreRecipe
            {
                OutputType = recipe.createItem.type,
                OutputStack = recipe.createItem.stack,
                Source = recipe
            };
            foreach (var ing in recipe.requiredItem)
                if (ing.type > ItemID.None)
                    core.Ingredients.Add(new CoreIngredient(ing.type, ing.stack));
            foreach (int t in recipe.requiredTile)
                if (t >= 0)
                    core.RequiredTiles.Add(t);
            foreach (int g in recipe.acceptedGroups)
                core.AcceptedGroups.Add(g);

            _coreByRecipe[recipe] = core;
            return core;
        }

        public IReadOnlyList<CoreRecipe> RecipesProducing(int itemType)
        {
            if (_producingCache.TryGetValue(itemType, out var list))
                return list;
            list = RecipeCacheSystem.Instance.GetRecipesFor(itemType).Select(ToCore).ToList();
            _producingCache[itemType] = list;
            return list;
        }

        public IReadOnlyList<CoreRecipe> AllRecipes
        {
            get
            {
                if (_allCore != null && _allCoreBuiltFor == Recipe.numRecipes)
                    return _allCore;

                var list = new List<CoreRecipe>();
                for (int i = 0; i < Recipe.numRecipes; i++)
                {
                    var r = Main.recipe[i];
                    if (r?.createItem == null || r.createItem.type <= ItemID.None)
                        continue;
                    list.Add(ToCore(r));
                }
                _allCore = list;
                _allCoreBuiltFor = Recipe.numRecipes;
                return _allCore;
            }
        }

        public bool GroupContains(int groupId, int itemType)
            => RecipeGroup.recipeGroups.TryGetValue(groupId, out var g) && g.ContainsItem(itemType);

        public IReadOnlyList<int> GroupValidItems(int groupId)
        {
            if (_groupItemsCache.TryGetValue(groupId, out var list))
                return list;
            list = RecipeGroup.recipeGroups.TryGetValue(groupId, out var g)
                ? g.ValidItems.ToList()
                : new List<int>();
            _groupItemsCache[groupId] = list;
            return list;
        }

        public bool IsStationSatisfied(int tile) => RecipeResolver.IsStationSatisfied(tile, _stations);

        public bool ConditionsMet(CoreRecipe recipe)
            => RecipeResolver.CheckRecipeConditionsPublic((Recipe)recipe.Source, _conditions);
    }
}

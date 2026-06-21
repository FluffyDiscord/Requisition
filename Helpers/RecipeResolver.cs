using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.Map;
using TerraStorage.Common;
using TerraStorage.Content.Items;
using TerraStorage.Systems;

namespace TerraStorage.Helpers
{
    // Represents a single intermediate crafting operation within a <see cref="CraftingPlan"/>.
    // Each step records what recipe is used, how many times it must be crafted, which items are
    // consumed and produced, and what stations that step requires.
    public class CraftingStep
    {
        public Recipe Recipe { get; set; }
        //Number of times the recipe must be executed to satisfy demand.
        public int CraftCount { get; set; }
        //Maps item type → total quantity consumed across all <see cref="CraftCount"/> executions.
        public Dictionary<int, int> Consumed { get; set; } = new();
        public int ProducedType { get; set; }
        public int ProducedCount { get; set; }
        public List<int> RequiredStations { get; set; } = new();
    }

    // The full resolved crafting plan for producing a target item. Contains all intermediate
    // <see cref="CraftingStep"/>s in bottom-up order, aggregate material requirements, and
    // feasibility information including missing stations or materials. 
    public class CraftingPlan
    {
        //Ordered list of intermediate crafting steps (dependencies first, target last).
        public List<CraftingStep> Steps { get; set; } = new();
        //Total raw materials needed across all steps (not counting intermediate products).
        public Dictionary<int, int> BaseMaterialsRequired { get; set; } = new();
        //Subset of <see cref="BaseMaterialsRequired"/> items that are not fully available.
        public Dictionary<int, int> BaseMaterialsMissing { get; set; } = new();
        public HashSet<int> RequiredStations { get; set; } = new();
        public HashSet<int> MissingStations { get; set; } = new();
        public bool IsFeasible { get; set; }
        public int FinalItemType { get; set; }
        public int FinalItemCount { get; set; }
        //Item was already in storage/inventory - no crafting steps needed.
        public bool IsDirectExtract => IsFeasible && Steps.Count == 0;
    }

    // Resolves multi-step crafting plans by recursively traversing the recipe tree,
    // checking material availability across storage and the player's inventory,
    // and verifying station/condition requirements against the connected network. 
    public static class RecipeResolver
    {
        // Limits recursive ingredient expansion to prevent infinite loops in circular recipe graphs
        public static int MaxDepth { get; set; } = 10;

        // Cache for tile name lookups (populated lazily, or pre-warmed via WarmTileCaches)
        private static readonly Dictionary<int, string> _tileNameCache = new();

        // Checks if a required tile type is satisfied by any station in the set,
        // accounting for adjTile equivalences (e.g. Mythril Anvil satisfies Anvil).
        public static bool IsStationSatisfied(int requiredTile, HashSet<int> availableStations)
        {
            if (availableStations.Contains(requiredTile))
                return true;

            // Check if any available station counts as the required tile via adjTile
            foreach (int station in availableStations)
            {
                foreach (int adj in AdjTileHelper.GetAdjTiles(station))
                {
                    if (adj == requiredTile)
                        return true;
                }
            }

            return false;
        }

        // True if every crafting-station tile a recipe requires is satisfied by the available
        // stations. Used to prefer fully-craftable recipes when several produce the same item.
        private static bool StationsAllSatisfied(Recipe recipe, HashSet<int> availableStations)
        {
            foreach (int t in recipe.requiredTile)
                if (t >= 0 && !IsStationSatisfied(t, availableStations))
                    return false;
            return true;
        }

        // Resolve a recipe recursively, determining what base materials are needed
        // and what intermediate crafting steps must be performed.
        // Checks crafting station requirements against available stations.
        public static CraftingPlan Resolve(int targetItemType, int quantity, IEnumerable<Guid> diskIds, HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions = null)
        {
            var available = GetAvailableItems(diskIds);
            availableConditions ??= new HashSet<CraftingCondition>();
            var plan = new CraftingPlan
            {
                FinalItemType = targetItemType,
                FinalItemCount = quantity
            };

            var resolving = new HashSet<int>(); // cycle detection
            bool feasible = ResolveRecursive(targetItemType, quantity, available, plan.Steps, resolving, 0, availableStations, availableConditions);

            // Collect required stations from successful steps only
            foreach (var step in plan.Steps)
            {
                foreach (int s in step.RequiredStations)
                    plan.RequiredStations.Add(s);
            }

            // Determine which stations are missing
            foreach (int stationType in plan.RequiredStations)
            {
                if (!IsStationSatisfied(stationType, availableStations))
                    plan.MissingStations.Add(stationType);
            }

            plan.IsFeasible = feasible && plan.MissingStations.Count == 0;

            // Calculate base materials
            CalculateBaseMaterials(plan, diskIds);

            return plan;
        }

        // Get the display name for a tile type (for UI display of station requirements).
        // Caches results for performance. 
        public static string GetTileName(int tileType)
        {
            if (_tileNameCache.TryGetValue(tileType, out string cached))
                return cached;

            // Reuse GetTileItemType to avoid a second full item scan
            int itemType = GetTileItemType(tileType);
            if (itemType > 0)
            {
                var tempItem = new Item();
                tempItem.SetDefaults(itemType);
                _tileNameCache[tileType] = tempItem.Name;
                return tempItem.Name;
            }

            // No item places this tile (e.g. Demon Altar). Use Terraria's map legend name.
            string mapName = Lang.GetMapObjectName(MapHelper.TileToLookup(tileType, 0));
            if (!string.IsNullOrWhiteSpace(mapName))
            {
                _tileNameCache[tileType] = mapName;
                return mapName;
            }

            string fallback = $"Tile #{tileType}";
            _tileNameCache[tileType] = fallback;
            return fallback;
        }

        // Get all available recipes that can be crafted with items in storage.
        // Filters by available crafting stations.
        public static List<Recipe> GetAvailableRecipes(IEnumerable<Guid> diskIds, HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions = null)
        {
            var available = GetAvailableItems(diskIds);
            availableConditions ??= new HashSet<CraftingCondition>();
            var results = new List<Recipe>();

            for (int i = 0; i < Recipe.numRecipes; i++)
            {
                var recipe = Main.recipe[i];
                if (recipe?.createItem == null || recipe.createItem.type <= ItemID.None)
                    continue;
                if (recipe.createItem.Name == "Recipe Not Found")
                    continue;

                // Check crafting station requirements
                bool stationsMet = true;
                foreach (int tileType in recipe.requiredTile)
                {
                    if (tileType >= 0 && !IsStationSatisfied(tileType, availableStations))
                    {
                        stationsMet = false;
                        break;
                    }
                }
                if (!stationsMet)
                    continue;

                // Check ingredient availability
                bool canCraft = true;
                foreach (var ingredient in recipe.requiredItem)
                {
                    if (ingredient.type <= ItemID.None)
                        continue;

                    int needed = ingredient.stack;
                    bool found = false;

                    if (available.TryGetValue(ingredient.type, out int have) && have >= needed)
                        found = true;

                    // Check recipe groups
                    if (!found)
                    {
                        foreach (int groupId in recipe.acceptedGroups)
                        {
                            var group = RecipeGroup.recipeGroups[groupId];
                            if (group.ContainsItem(ingredient.type))
                            {
                                foreach (int validItem in group.ValidItems)
                                {
                                    if (available.TryGetValue(validItem, out int groupHave) && groupHave >= needed)
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            if (found)
                                break;
                        }
                    }

                    if (!found)
                    {
                        canCraft = false;
                        break;
                    }
                }

                bool conditionsMet = CheckRecipeConditions(recipe, availableConditions);
                if (canCraft && conditionsMet)
                    results.Add(recipe);
            }

            return results;
        }

        // Checks if the player or the network meets the special conditions for a recipe. 
        //Public accessor for lightweight canCraft updates.
        public static bool CheckRecipeConditionsPublic(Recipe recipe, HashSet<CraftingCondition> conditions)
            => CheckRecipeConditions(recipe, conditions);

        private static bool CheckRecipeConditions(Recipe recipe, HashSet<CraftingCondition> availableConditions)
        {
            foreach (var condition in recipe.Conditions)
            {
                // First, check the original predicate (player's environment)
                if (condition.Predicate())
                    continue;

                // If that fails, check if a network provider satisfies it
                bool networkSatisfied = false;
                if (condition == Condition.NearWater && availableConditions.Contains(CraftingCondition.NearWater))
                    networkSatisfied = true;
                else if (condition == Condition.NearLava && availableConditions.Contains(CraftingCondition.NearLava))
                    networkSatisfied = true;
                else if (condition == Condition.NearHoney && availableConditions.Contains(CraftingCondition.NearHoney))
                    networkSatisfied = true;
                else if (condition == Condition.InGraveyard && availableConditions.Contains(CraftingCondition.InGraveyard))
                    networkSatisfied = true;
                else if (condition == Condition.InSnow && availableConditions.Contains(CraftingCondition.InSnow))
                    networkSatisfied = true;
                if (!networkSatisfied)
                    return false; // Condition not met by player or network
            }

            return true;
        }

        // Get all recipes. Returns (recipe, canCraft) where canCraft is true only if
        // both station requirements AND ingredient requirements are met.
        // Includes recursive craftability (expensive). Use <see cref="GetAllRecipesDirect"/>
        // for just the direct-check pass.
        public static List<(Recipe recipe, bool canCraft)> GetAllRecipesWithStations(
            IEnumerable<Guid> diskIds, HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions = null)
        {
            var diskList = diskIds as List<Guid> ?? diskIds.ToList();
            var available = GetAvailableItems(diskList);
            availableConditions ??= new HashSet<CraftingCondition>();

            var results = GetAllRecipesDirect(available, availableStations, availableConditions);
            ApplyRecursiveCraftability(results, available, availableStations, availableConditions);
            return results;
        }

        // Fast first pass: returns all valid recipes with direct ingredient availability only.
        // No BFS reachability or recursive feasibility checks — O(n) single pass.
        public static List<(Recipe recipe, bool canCraft)> GetAllRecipesDirect(
            IEnumerable<Guid> diskIds, HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions = null)
        {
            var diskList = diskIds as List<Guid> ?? diskIds.ToList();
            var available = GetAvailableItems(diskList);
            availableConditions ??= new HashSet<CraftingCondition>();
            return GetAllRecipesDirect(available, availableStations, availableConditions);
        }

        private static List<(Recipe recipe, bool canCraft)> GetAllRecipesDirect(
            Dictionary<int, int> available, HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions)
        {
            var results = new List<(Recipe, bool)>();

            for (int i = 0; i < Recipe.numRecipes; i++)
            {
                var recipe = Main.recipe[i];
                if (recipe?.createItem == null || recipe.createItem.type <= ItemID.None)
                    continue;
                if (recipe.createItem.Name == "Recipe Not Found")
                    continue;

                bool stationsMet = true;
                foreach (int tileType in recipe.requiredTile)
                {
                    if (tileType >= 0 && !IsStationSatisfied(tileType, availableStations))
                    {
                        stationsMet = false;
                        break;
                    }
                }

                bool ingredientsMet = true;
                foreach (var ingredient in recipe.requiredItem)
                {
                    if (ingredient.type <= ItemID.None)
                        continue;

                    int needed = ingredient.stack;
                    bool found = false;

                    if (available.TryGetValue(ingredient.type, out int have) && have >= needed)
                        found = true;

                    if (!found)
                    {
                        foreach (int groupId in recipe.acceptedGroups)
                        {
                            var group = RecipeGroup.recipeGroups[groupId];
                            if (group.ContainsItem(ingredient.type))
                            {
                                foreach (int validItem in group.ValidItems)
                                {
                                    if (available.TryGetValue(validItem, out int groupHave) && groupHave >= needed)
                                    {
                                        found = true;
                                        break;
                                    }
                                }
                            }
                            if (found)
                                break;
                        }
                    }

                    if (!found)
                    {
                        ingredientsMet = false;
                        break;
                    }
                }

                bool conditionsMet = CheckRecipeConditions(recipe, availableConditions);
                bool canCraft = stationsMet && ingredientsMet && conditionsMet;
                results.Add((recipe, canCraft));
            }

            return results;
        }

        // Second pass: recomputes each recipe's craftability authoritatively (direct OR recursive),
        // promoting recipes whose ingredients are recursively feasible and demoting ones that are
        // no longer craftable. Call this after <see cref="GetAllRecipesDirect"/>; re-running it over
        // a fresh snapshot fully corrects stale flags. Can be driven incrementally across frames via
        // <see cref="ApplyRecursiveCraftabilityBatch"/>.
        public static void ApplyRecursiveCraftability(
            List<(Recipe recipe, bool canCraft)> results,
            Dictionary<int, int> available,
            HashSet<int> availableStations,
            HashSet<CraftingCondition> availableConditions)
        {
            var reachable = ComputeReachableTypes(available, availableStations, availableConditions);
            var ingCache = new Dictionary<(int type, int stack), bool>();
            for (int i = 0; i < results.Count; i++)
                CheckRecursiveAt(results, i, reachable, available, availableStations, availableConditions, ingCache);
        }

        // Processes a batch of the recursive craftability pass. Returns the index to resume from
        // next frame, or -1 if complete. Caller should pass startIndex=0 on first call, then
        // feed the returned value back on subsequent frames.
        // <param name="reachable">Pre-computed reachable set from <see cref="ComputeReachableTypes"/>.</param>
        // <param name="ingCache">Shared ingredient cache — pass the same instance across batches.</param>
        // <param name="anyFlipped">Set to true if any recipe's canCraft changed in this batch.</param>
        public static int ApplyRecursiveCraftabilityBatch(
            List<(Recipe recipe, bool canCraft)> results,
            int startIndex, int batchSize,
            HashSet<int> reachable,
            Dictionary<int, int> available,
            HashSet<int> availableStations,
            HashSet<CraftingCondition> availableConditions,
            Dictionary<(int type, int stack), bool> ingCache,
            out bool anyFlipped)
        {
            anyFlipped = false;
            int end = Math.Min(startIndex + batchSize, results.Count);
            for (int i = startIndex; i < end; i++)
            {
                bool wasCraftable = results[i].canCraft;
                CheckRecursiveAt(results, i, reachable, available, availableStations, availableConditions, ingCache);
                // The check is authoritative (it can demote as well as promote), so surface any
                // change — a recipe that became uncraftable must leave the list too.
                if (wasCraftable != results[i].canCraft)
                    anyFlipped = true;
            }
            return end >= results.Count ? -1 : end;
        }

        //Expose BFS reachability computation for deferred use.
        public static HashSet<int> ComputeReachableTypesPublic(
            Dictionary<int, int> available, HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions)
            => ComputeReachableTypes(available, availableStations, availableConditions);

        // True if a single ingredient is satisfied directly from stock — its own type or a
        // recipe-group substitute — ignoring any sub-crafting. Shared by the direct and
        // recursive craftability checks so the two passes agree on what "in stock" means.
        // <paramref name="viaGroup"/> is set true when satisfaction relied on a recipe-group
        // substitute (not the ingredient's own type) — the only way two directly-stocked
        // ingredients can contend for the same base stock, so the caller knows to confirm.
        private static bool IngredientSatisfiedDirectly(Recipe recipe, int ingredientType, int needed, Dictionary<int, int> available, out bool viaGroup)
        {
            viaGroup = false;
            if (available.TryGetValue(ingredientType, out int have) && have >= needed)
                return true;

            foreach (int groupId in recipe.acceptedGroups)
            {
                var group = RecipeGroup.recipeGroups[groupId];
                if (!group.ContainsItem(ingredientType)) continue;
                foreach (int validItem in group.ValidItems)
                {
                    if (available.TryGetValue(validItem, out int groupHave) && groupHave >= needed)
                    {
                        viaGroup = true;
                        return true;
                    }
                }
            }
            return false;
        }

        // Confirms a recipe is craftable by simulating ALL of its ingredients against ONE
        // shared, deducting clone of the snapshot — so two ingredients that draw on the same
        // base material cannot each be counted against the full stock (the overcount that made
        // two-way / shared-material recipes show as craftable when they are not). Mirrors the
        // per-recipe block inside ResolveRecursive; returns false as soon as an ingredient cannot
        // be met once earlier ingredients have taken their share of the pool.
        private static bool IsRecipeFeasibleShared(Recipe recipe, Dictionary<int, int> availableSnapshot,
            HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions)
        {
            var available = new Dictionary<int, int>(availableSnapshot);
            var steps = new List<CraftingStep>();
            // Seed the cycle guard with the output type so a recipe cannot consume its own
            // product to make itself (matches ResolveRecursive's per-type guard).
            var resolving = new HashSet<int> { recipe.createItem.type };

            foreach (var ingredient in recipe.requiredItem)
            {
                if (ingredient.type <= ItemID.None) continue;
                int resolvedType = ResolveIngredientType(recipe, ingredient.type, available);
                if (!ResolveRecursive(resolvedType, ingredient.stack, available, steps, resolving, 1,
                        availableStations, availableConditions))
                    return false;
            }
            return true;
        }

        // Computes the AUTHORITATIVE craftability of results[i] — direct OR recursive — and
        // stores it. Both promotes newly-craftable recipes and demotes ones no longer craftable,
        // so re-running the pass over a fresh storage snapshot fully corrects stale flags.
        //
        // Recursive feasibility is two-stage: a cheap per-ingredient "feasible in isolation from
        // full stock" pre-filter (memoized in ingCache, shared across the pass), then — only when
        // a recipe has 2+ ingredients that could compete for a shared base material — a single
        // shared-pool confirm (IsRecipeFeasibleShared). The pre-filter alone over-counts shared
        // materials; the confirm is what makes the list flag match what an actual craft would do.
        private static void CheckRecursiveAt(
            List<(Recipe recipe, bool canCraft)> results, int i,
            HashSet<int> reachable,
            Dictionary<int, int> available,
            HashSet<int> availableStations,
            HashSet<CraftingCondition> availableConditions,
            Dictionary<(int type, int stack), bool> ingCache)
        {
            var recipe = results[i].recipe;

            // The reachable set (closed over in-stock items) is a superset of every directly- and
            // recursively-craftable output, so an unreachable output is craftable by neither.
            if (!reachable.Contains(recipe.createItem.type)) { results[i] = (recipe, false); return; }

            bool stationsMet = true;
            foreach (int t in recipe.requiredTile)
                if (t >= 0 && !IsStationSatisfied(t, availableStations)) { stationsMet = false; break; }
            if (!stationsMet || !CheckRecipeConditions(recipe, availableConditions)) { results[i] = (recipe, false); return; }

            bool allDirect = true;
            bool usedGroupSubstitute = false;
            int realIngredients = 0;
            foreach (var ing in recipe.requiredItem)
            {
                if (ing.type <= ItemID.None) continue;
                realIngredients++;

                if (IngredientSatisfiedDirectly(recipe, ing.type, ing.stack, available, out bool viaGroup))
                {
                    if (viaGroup) usedGroupSubstitute = true;
                    continue;
                }

                allDirect = false;

                // Pre-filter: this ingredient must be feasible in isolation from full stock.
                // If it fails alone it cannot pass while sharing — reject cheaply.
                var key = (ing.type, ing.stack);
                if (!ingCache.TryGetValue(key, out bool ok))
                {
                    ok = IsFeasibleFromSnapshot(ing.type, ing.stack, available, availableStations, availableConditions);
                    ingCache[key] = ok;
                }
                if (!ok) { results[i] = (recipe, false); return; }
            }

            // No double-count is possible without a sibling to contend with, so a lone ingredient
            // (or a single real ingredient) is craftable as soon as it is satisfiable. With 2+
            // ingredients, confirm against a shared deducting pool whenever they could compete for
            // the same stock: either a sub-craft is involved (!allDirect) OR an ingredient was met
            // via a recipe-group substitute (two group slots can drain one shared pool).
            bool needsSharedConfirm = realIngredients >= 2 && (!allDirect || usedGroupSubstitute);
            if (needsSharedConfirm &&
                !IsRecipeFeasibleShared(recipe, available, availableStations, availableConditions))
            {
                results[i] = (recipe, false);
                return;
            }

            results[i] = (recipe, true);
        }

        // Computes the set of item types that are transitively producible given the items
        // currently in storage, by running a BFS fixpoint over the recipe graph.
        // Ignores exact quantities — suitable for recipe-list visibility only.
        private static HashSet<int> ComputeReachableTypes(
            Dictionary<int, int> available,
            HashSet<int> availableStations,
            HashSet<CraftingCondition> availableConditions)
        {
            // Pre-filter to recipes whose stations and conditions are met — these don't change
            // between BFS iterations so checking them once here avoids repeating the work.
            var eligible = new List<Recipe>();
            for (int i = 0; i < Recipe.numRecipes; i++)
            {
                var r = Main.recipe[i];
                if (r?.createItem == null || r.createItem.type <= ItemID.None) continue;

                bool ok = true;
                foreach (int t in r.requiredTile)
                    if (t >= 0 && !IsStationSatisfied(t, availableStations)) { ok = false; break; }
                if (!ok) continue;

                if (!CheckRecipeConditions(r, availableConditions)) continue;

                eligible.Add(r);
            }

            // Seed only with item types actually in stock (count > 0) — the available dict can
            // contain zero-count entries from partial consumption in ResolveRecursive, and seeding
            // those in would cause false positives in the BFS ingredient checks.
            var reachable = new HashSet<int>();
            foreach (var kvp in available)
                if (kvp.Value > 0) reachable.Add(kvp.Key);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var r in eligible)
                {
                    if (reachable.Contains(r.createItem.type)) continue;

                    bool ingredientsMet = true;
                    foreach (var ing in r.requiredItem)
                    {
                        if (ing.type <= ItemID.None) continue;
                        if (reachable.Contains(ing.type)) continue;

                        bool foundInGroup = false;
                        foreach (int groupId in r.acceptedGroups)
                        {
                            var group = RecipeGroup.recipeGroups[groupId];
                            if (!group.ContainsItem(ing.type)) continue;
                            foreach (int valid in group.ValidItems)
                                if (reachable.Contains(valid)) { foundInGroup = true; break; }
                            if (foundInGroup) break;
                        }
                        if (!foundInGroup) { ingredientsMet = false; break; }
                    }

                    if (ingredientsMet)
                    {
                        reachable.Add(r.createItem.type);
                        changed = true;
                    }
                }
            }
            return reachable;
        }

        // Get the item type that places a given tile type. Returns -1 if not found.
        // Cached for performance. Use <see cref="RegisterTileDisplay"/> to map vanilla
        // non-placeable tile types (e.g. Demon Altar) to a representative item.
        private static readonly Dictionary<int, int> _tileToItemCache = new();
        private static readonly Dictionary<int, int> _tileDisplayOverrides = new();

        // Registers an explicit item to display for a tile type that has no directly
        // placeable item (e.g. TileID.DemonAltar → DemonAltarItem).
        // Call from PostSetupContent after ModContent types are resolved. 
        public static void RegisterTileDisplay(int tileType, int itemType)
        {
            _tileDisplayOverrides[tileType] = itemType;
        }

        // Builds the full tile→item mapping in a single pass over all items.
        // Call once after PostSetupContent (e.g. on world load) to avoid per-tile
        // linear scans that cause hitching on first hover.
        public static void WarmTileCaches()
        {
            if (_tileToItemCache.Count > 0) return; // already warmed

            // Apply overrides first so they take priority
            foreach (var kvp in _tileDisplayOverrides)
                _tileToItemCache[kvp.Key] = kvp.Value;

            // Single pass: map every tile type to the first item that places it
            for (int i = 1; i < ItemLoader.ItemCount; i++)
            {
                var tempItem = new Item();
                tempItem.SetDefaults(i);
                int tile = tempItem.createTile;
                if (tile >= 0 && !_tileToItemCache.ContainsKey(tile))
                    _tileToItemCache[tile] = i;
            }

            // Also pre-warm tile name cache from the results
            foreach (var kvp in _tileToItemCache)
            {
                if (kvp.Value > 0 && !_tileNameCache.ContainsKey(kvp.Key))
                {
                    var tempItem = new Item();
                    tempItem.SetDefaults(kvp.Value);
                    _tileNameCache[kvp.Key] = tempItem.Name;
                }
            }
        }

        public static int GetTileItemType(int tileType)
        {
            if (_tileToItemCache.TryGetValue(tileType, out int itemType))
                return itemType;

            if (_tileDisplayOverrides.TryGetValue(tileType, out int overrideType))
            {
                _tileToItemCache[tileType] = overrideType;
                return overrideType;
            }

            // Fallback for tiles not found during warm-up (shouldn't happen after WarmTileCaches)
            for (int i = 1; i < ItemLoader.ItemCount; i++)
            {
                var tempItem = new Item();
                tempItem.SetDefaults(i);
                if (tempItem.createTile == tileType)
                {
                    _tileToItemCache[tileType] = i;
                    return i;
                }
            }

            _tileToItemCache[tileType] = -1;
            return -1;
        }

        // Like Resolve but ignores existing stock of the target item, forcing actual crafting.
        // Returns null if the item cannot be crafted with available ingredients.
        public static CraftingPlan ResolveForceCraft(int targetItemType, int quantity, IEnumerable<Guid> diskIds, HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions = null)
        {
            var available = GetAvailableItems(diskIds);
            availableConditions ??= new HashSet<CraftingCondition>();
            // Remove existing target items so resolver must craft them
            available.Remove(targetItemType);

            var plan = new CraftingPlan
            {
                FinalItemType = targetItemType,
                FinalItemCount = quantity
            };

            var resolving = new HashSet<int>();
            bool feasible = ResolveRecursive(targetItemType, quantity, available, plan.Steps, resolving, 0, availableStations, availableConditions);

            foreach (var step in plan.Steps)
            {
                foreach (int s in step.RequiredStations)
                    plan.RequiredStations.Add(s);
            }

            foreach (int stationType in plan.RequiredStations)
            {
                if (!IsStationSatisfied(stationType, availableStations))
                    plan.MissingStations.Add(stationType);
            }

            plan.IsFeasible = feasible && plan.MissingStations.Count == 0 && plan.Steps.Count > 0;
            if (plan.IsFeasible)
                CalculateBaseMaterials(plan, diskIds);

            return plan.IsFeasible ? plan : null;
        }

        // Given an ingredient type and the recipe's accepted groups, returns the best item type
        // to actually consume — preferring the ingredient's own type, falling back to any recipe
        // group substitute that is already in <paramref name="available"/>. 
        private static int ResolveIngredientType(Recipe recipe, int ingredientType, Dictionary<int, int> available)
        {
            // If we already have enough of the exact type, use it
            if (available.TryGetValue(ingredientType, out int have) && have > 0)
                return ingredientType;

            // Look for a recipe-group substitute that is already in stock
            foreach (int groupId in recipe.acceptedGroups)
            {
                var group = RecipeGroup.recipeGroups[groupId];
                if (!group.ContainsItem(ingredientType))
                    continue;

                foreach (int validItem in group.ValidItems)
                {
                    if (validItem == ingredientType) continue;
                    if (available.TryGetValue(validItem, out int altHave) && altHave > 0)
                        return validItem;
                }
            }

            return ingredientType;
        }

        // Recursively attempts to satisfy a demand for <paramref name="needed"/> units of
        // <paramref name="itemType"/> using the mutable <paramref name="available"/> dictionary.
        // On success the required quantities are deducted from <paramref name="available"/> and
        // the necessary <see cref="CraftingStep"/>s are appended to <paramref name="steps"/>.
        // Returns false if the demand cannot be met with the current stock or recipes.
        private static bool ResolveRecursive(int itemType, int needed, Dictionary<int, int> available,
            List<CraftingStep> steps, HashSet<int> resolving, int depth,
            HashSet<int> availableStations, HashSet<CraftingCondition> availableConditions)
        {
            if (depth > MaxDepth)
                return false;

            // Check if we already have enough in the simulated available pool
            if (available.TryGetValue(itemType, out int have) && have >= needed)
            {
                available[itemType] -= needed;
                return true;
            }

            // Partially consume existing stock; only craft enough to cover the deficit
            int deficit = needed;
            if (have > 0)
            {
                deficit -= have;
                available[itemType] = 0;
            }

            // Cycle detection: if we're already in the process of resolving this type,
            // attempting it again would loop forever
            if (!resolving.Add(itemType))
                return false;

            var cache = RecipeCacheSystem.Instance;
            var recipes = cache.GetRecipesFor(itemType);

            // Prefer recipes whose required stations are all available, so a station-missing recipe
            // never wins over a fully craftable variant of the same item (which produced a false
            // "missing stations" when an available-station recipe existed). This is a stable REORDER,
            // not a filter: station-missing recipes stay at the end as fallback so a genuinely
            // station-missing item still resolves and reports its missing station.
            IEnumerable<Recipe> ordered = recipes;
            if (recipes.Count > 1)
                ordered = recipes.OrderByDescending(r => StationsAllSatisfied(r, availableStations));

            foreach (var recipe in ordered)
            {
                // Check for special conditions against both player and network
                if (!CheckRecipeConditions(recipe, availableConditions))
                    continue;

                // Collect station requirements for this recipe
                var stepStations = new List<int>();
                foreach (int tileType in recipe.requiredTile)
                {
                    if (tileType < 0)
                        continue;
                    stepStations.Add(tileType);
                }

                // Ceiling division: if the recipe produces 3 per craft and we need 7, we must craft 3 times
                int craftsNeeded = (int)Math.Ceiling((double)deficit / recipe.createItem.stack);

                // Snapshot available inventory before trying this recipe so we can roll back on failure
                var availBackup = new Dictionary<int, int>(available);
                var tempSteps = new List<CraftingStep>();
                bool allResolved = true;
                var consumed = new Dictionary<int, int>();

                foreach (var ingredient in recipe.requiredItem)
                {
                    if (ingredient.type <= ItemID.None)
                        continue;

                    // Resolve to the best available type, respecting recipe groups
                    int resolvedType = ResolveIngredientType(recipe, ingredient.type, available);
                    int ingredientNeeded = ingredient.stack * craftsNeeded;
                    consumed[resolvedType] = ingredientNeeded;

                    if (!ResolveRecursive(resolvedType, ingredientNeeded, available, tempSteps, resolving, depth + 1,
                        availableStations, availableConditions))
                    {
                        // This recipe path failed — restore the availability snapshot and try the next recipe
                        allResolved = false;
                        available.Clear();
                        foreach (var kvp in availBackup)
                            available[kvp.Key] = kvp.Value;
                        break;
                    }
                }

                if (allResolved)
                {
                    steps.AddRange(tempSteps);

                    int produced = craftsNeeded * recipe.createItem.stack;
                    steps.Add(new CraftingStep
                    {
                        Recipe = recipe,
                        CraftCount = craftsNeeded,
                        Consumed = consumed,
                        ProducedType = itemType,
                        ProducedCount = produced,
                        RequiredStations = stepStations
                    });

                    // Any overproduction (recipe yields more than needed) is added back to the available pool
                    // so subsequent steps can use it as free materials
                    int excess = produced - deficit;
                    if (excess > 0)
                    {
                        if (!available.ContainsKey(itemType))
                            available[itemType] = 0;
                        available[itemType] += excess;
                    }

                    resolving.Remove(itemType);
                    return true;
                }
            }

            resolving.Remove(itemType);
            return false;
        }

        // Checks feasibility using an already-computed item snapshot rather than re-scanning
        // disk data. The snapshot is cloned before use so the original is not consumed.
        // Used by <see cref="GetAllRecipesWithStations"/> to avoid redundant disk scans in
        // the second pass.
        private static bool IsFeasibleFromSnapshot(int targetItemType, int quantity,
            Dictionary<int, int> availableSnapshot, HashSet<int> availableStations,
            HashSet<CraftingCondition> availableConditions)
        {
            var available = new Dictionary<int, int>(availableSnapshot);
            var steps = new List<CraftingStep>();
            var resolving = new HashSet<int>();
            bool feasible = ResolveRecursive(targetItemType, quantity, available, steps, resolving, 0, availableStations, availableConditions);
            if (!feasible) return false;

            foreach (var step in steps)
                foreach (int s in step.RequiredStations)
                    if (!IsStationSatisfied(s, availableStations)) return false;

            return true;
        }

        // Builds a mutable snapshot of all items available for crafting from the given disks.
        // This snapshot is modified in-place by <see cref="ResolveRecursive"/> to simulate consumption. 
        // Builds a dictionary of itemType → total count across all given disks.
        // Public so the crafting panel can use it for lightweight canCraft updates. 
        public static Dictionary<int, int> GetAvailableItemsPublic(IEnumerable<Guid> diskIds)
            => GetAvailableItems(diskIds);

        private static Dictionary<int, int> GetAvailableItems(IEnumerable<Guid> diskIds)
        {
            var items = new Dictionary<int, int>();
            var consolidated = StorageWorldSystem.Instance.GetConsolidatedItems(diskIds);

            foreach (var ci in consolidated)
            {
                if (!items.ContainsKey(ci.ItemType))
                    items[ci.ItemType] = 0;
                items[ci.ItemType] += ci.TotalCount;
            }

            return items;
        }

        // Populates <see cref="CraftingPlan.BaseMaterialsRequired"/> and
        // <see cref="CraftingPlan.BaseMaterialsMissing"/> by computing the net material demand
        // after subtracting intermediate products produced by earlier steps.
        private static void CalculateBaseMaterials(CraftingPlan plan, IEnumerable<Guid> diskIds)
        {
            var produced = new Dictionary<int, int>();
            var consumed = new Dictionary<int, int>();

            foreach (var step in plan.Steps)
            {
                foreach (var kvp in step.Consumed)
                {
                    if (!consumed.ContainsKey(kvp.Key))
                        consumed[kvp.Key] = 0;
                    consumed[kvp.Key] += kvp.Value;
                }

                if (!produced.ContainsKey(step.ProducedType))
                    produced[step.ProducedType] = 0;
                produced[step.ProducedType] += step.ProducedCount;
            }

            foreach (var kvp in consumed)
            {
                // Subtract any amount already produced by an earlier step in the chain
                produced.TryGetValue(kvp.Key, out int prod);
                int netRequired = kvp.Value - prod;
                if (netRequired > 0)
                {
                    plan.BaseMaterialsRequired[kvp.Key] = netRequired;

                    int inStorage = StorageWorldSystem.Instance.CountItem(diskIds, kvp.Key);
                    if (inStorage < netRequired)
                        plan.BaseMaterialsMissing[kvp.Key] = netRequired - inStorage;
                }
            }
        }

        private static void ExtractFromBoth(List<Guid> diskList, int itemType, int amount)
        {
            StorageWorldSystem.Instance.ExtractItem(diskList, itemType, amount);
        }

        // Execute a crafting plan, consuming items from storage and player inventory,
        // producing the result back into storage. 
        public static Item ExecutePlan(CraftingPlan plan, IEnumerable<Guid> diskIds, bool cleanCraft = true)
        {
            if (!plan.IsFeasible)
                return new Item();

            var diskList = diskIds.ToList();
            Item finalResult = new Item();

            for (int i = 0; i < plan.Steps.Count; i++)
            {
                var step = plan.Steps[i];
                bool isFinalStep = i == plan.Steps.Count - 1;

                // Detect disk upgrade steps (e.g. Tier1 → Tier2) before consuming anything.
                // ExecutePlan bypasses Terraria's crafting pipeline entirely, so the
                // AddOnCraftCallback registered in DiskRecipes never fires here. We must
                // replicate the same GUID-transfer logic manually.
                Guid sourceGuid = Guid.Empty;
                if (IsDiskUpgradeStep(step, out int sourceDiskItemType))
                {
                    // Capture the source disk's GUID from storage before extraction removes it.
                    sourceGuid = FindDiskGuidInStorage(diskList, sourceDiskItemType);
                }

                foreach (var kvp in step.Consumed)
                    ExtractFromBoth(diskList, kvp.Key, kvp.Value);

                var produced = new Item();
                produced.SetDefaults(step.ProducedType);
                produced.stack = step.ProducedCount;

                // Transfer the source GUID to the newly produced disk and upgrade its
                // DiskData tier in-place, preserving all previously stored items.
                if (sourceGuid != Guid.Empty && produced.ModItem is StorageDiskBase resultDisk)
                {
                    resultDisk.AssignDiskId(sourceGuid);
                    StorageWorldSystem.Instance.UpgradeDisk(sourceGuid, resultDisk.Tier);
                }

                if (isFinalStep)
                {
                    // Return the final item directly to the caller — never route it through
                    // storage first. Routing through storage caused a silent item loss when
                    // storage was full: the insert would fail, the subsequent extract would
                    // return nothing, and the caller would receive an air item even though
                    // ingredients had already been consumed.
                    // If the recipe produced more items than requested (batch rounding),
                    // store the excess. Losing excess on a full store is acceptable.
                    int excess = step.ProducedCount - plan.FinalItemCount;
                    if (excess > 0)
                    {
                        var excessItem = produced.Clone();
                        excessItem.stack = excess;
                        StorageWorldSystem.Instance.InsertItem(diskList, excessItem);
                        produced.stack = plan.FinalItemCount;
                    }
                    finalResult = produced;
                }
                else
                {
                    // Intermediate step: insert into storage so the next step can consume it.
                    StorageWorldSystem.Instance.InsertItem(diskList, produced);
                }
            }

            // Apply vanilla crafting simulation (prefix rolling + mod hooks) on the
            // final result, unless Clean Craft is enabled or this is a disk upgrade.
            // Runs after all steps complete so ingredients are safely consumed even if
            // a mod hook throws an exception.
            if (!cleanCraft && !finalResult.IsAir && plan.Steps.Count > 0
                && !(finalResult.ModItem is StorageDiskBase))
            {
                var finalStep = plan.Steps[^1];
                if (finalStep.Recipe != null)
                {
                    int savedStack = finalResult.stack;

                    // Roll a random prefix (no-op for items that can't have prefixes)
                    try { finalResult.Prefix(-1); }
                    catch { /* prefix rolling failed — item stays unmodified */ }
                    finalResult.stack = savedStack;

                    // Build consumed items list for mod hooks
                    var consumedItems = new List<Item>();
                    foreach (var kvp in finalStep.Consumed)
                    {
                        var ci = new Item();
                        ci.SetDefaults(kvp.Key);
                        ci.stack = kvp.Value;
                        consumedItems.Add(ci);
                    }

                    // Fire OnCreated hooks with per-hook exception isolation.
                    // tModLoader's ItemLoader.OnCreated (also called internally by
                    // RecipeLoader.OnCraft) iterates all GlobalItem hooks in a single
                    // loop without try-catch — one mod throwing prevents all subsequent
                    // hooks from running (e.g. TerraCards' CardSystemItem never getting
                    // its card slots initialized). Iterating manually ensures every
                    // GlobalItem.OnCreated is called regardless of earlier failures.
                    var creationContext = new RecipeItemCreationContext(finalStep.Recipe, consumedItems, finalResult);
                    foreach (var gi in finalResult.EntityGlobals)
                    {
                        if (gi == null) continue;
                        try { gi.OnCreated(finalResult, creationContext); }
                        catch { }
                    }
                    if (finalResult.ModItem != null)
                    {
                        try { finalResult.ModItem.OnCreated(creationContext); }
                        catch { }
                    }

                    finalResult.stack = savedStack;
                }
            }

            return finalResult;
        }

        // Returns true if <paramref name="step"/> produces a Storage Disk by consuming a
        // lower-tier disk as an ingredient. Sets <paramref name="sourceDiskItemType"/> to
        // the consumed disk's item type so the caller can look up its GUID.
        // 
        private static bool IsDiskUpgradeStep(CraftingStep step, out int sourceDiskItemType)
        {
            sourceDiskItemType = -1;

            // Result must be a Storage Disk
            var resultCheck = new Item();
            resultCheck.SetDefaults(step.ProducedType);
            if (resultCheck.ModItem is not StorageDiskBase)
                return false;

            // One of the consumed items must also be a Storage Disk (the source being upgraded)
            foreach (int consumedType in step.Consumed.Keys)
            {
                var temp = new Item();
                temp.SetDefaults(consumedType);
                if (temp.ModItem is StorageDiskBase)
                {
                    sourceDiskItemType = consumedType;
                    return true;
                }
            }

            return false;
        }

        // Scans the storage network for a stored disk item of <paramref name="diskItemType"/>
        // and returns its <see cref="StorageDiskBase.DiskId"/>.
        // Returns <see cref="Guid.Empty"/> if none is found.
        private static Guid FindDiskGuidInStorage(List<Guid> diskList, int diskItemType)
        {
            var sys = StorageWorldSystem.Instance;
            foreach (var diskId in diskList)
            {
                var data = sys.GetDiskData(diskId);
                if (data == null) continue;

                foreach (var stored in data.Items)
                {
                    if (stored.ItemType != diskItemType || stored.ModData == null) continue;

                    // Reconstruct the ModItem temporarily to read the GUID via LoadData
                    var temp = new Item();
                    temp.SetDefaults(diskItemType);
                    if (temp.ModItem is StorageDiskBase tempDisk)
                    {
                        tempDisk.LoadData(stored.ModData);
                        if (tempDisk.DiskId != Guid.Empty)
                            return tempDisk.DiskId;
                    }
                }
            }
            return Guid.Empty;
        }

        public static string GetGroupName(int groupId)
        {
            if (RecipeGroup.recipeGroups.TryGetValue(groupId, out var grp))
                return "Any " + Lang.GetItemNameValue(grp.IconicItemId);
            return "?";
        }

        public static string GetGroupItemNames(int groupId)
        {
            if (!RecipeGroup.recipeGroups.TryGetValue(groupId, out var grp))
                return "?";
            var names = new System.Text.StringBuilder();
            foreach (int v in grp.ValidItems)
            {
                if (names.Length > 0) names.Append(" / ");
                names.Append(Lang.GetItemNameValue(v));
            }
            return names.ToString();
        }
    }
}

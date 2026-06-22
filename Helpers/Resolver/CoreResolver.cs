using System;
using System.Collections.Generic;
using System.Linq;

namespace TerraStorage.Helpers.Resolver
{
    // The pure crafting-resolution algorithm. Operates entirely through <see cref="IRecipeEnvironment"/>
    // and plain dictionaries/sets, so it carries no Terraria dependency and can be unit-tested with
    // synthetic recipe fixtures. The Terraria-facing RecipeResolver builds an environment adapter and
    // delegates every recursive decision here; the unit tests build a fake environment and call the
    // same methods, so the tested code is the shipped code.
    public sealed class CoreResolver
    {
        private readonly IRecipeEnvironment _env;
        public int MaxDepth = 10;

        public CoreResolver(IRecipeEnvironment env)
        {
            _env = env;
        }

        public bool IsStationSatisfied(int tile) => _env.IsStationSatisfied(tile);

        // True if every station tile this recipe needs is available.
        public bool StationsAllSatisfied(CoreRecipe recipe)
        {
            foreach (int t in recipe.RequiredTiles)
                if (!_env.IsStationSatisfied(t))
                    return false;
            return true;
        }

        // True if a single ingredient is satisfied directly from stock — own type or a recipe-group
        // substitute — ignoring sub-crafting. viaGroup is set when satisfaction relied on a substitute.
        private bool IngredientSatisfiedDirectly(CoreRecipe recipe, int ingredientType, int needed,
            Dictionary<int, int> available, out bool viaGroup)
        {
            viaGroup = false;
            if (available.TryGetValue(ingredientType, out int have) && have >= needed)
                return true;

            foreach (int groupId in recipe.AcceptedGroups)
            {
                if (!_env.GroupContains(groupId, ingredientType)) continue;
                foreach (int validItem in _env.GroupValidItems(groupId))
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

        // Chooses which concrete item to consume for an ingredient: its own type if in stock,
        // else a recipe-group substitute that is in stock, else its own type.
        private int ResolveIngredientType(CoreRecipe recipe, int ingredientType, Dictionary<int, int> available)
        {
            if (available.TryGetValue(ingredientType, out int have) && have > 0)
                return ingredientType;

            foreach (int groupId in recipe.AcceptedGroups)
            {
                if (!_env.GroupContains(groupId, ingredientType)) continue;
                foreach (int validItem in _env.GroupValidItems(groupId))
                {
                    if (validItem == ingredientType) continue;
                    if (available.TryGetValue(validItem, out int altHave) && altHave > 0)
                        return validItem;
                }
            }
            return ingredientType;
        }

        // Recursively satisfies a demand for `needed` units of `itemType` against the mutable
        // `available` pool, appending the required steps. Deducts consumed quantities from the pool
        // so siblings cannot each claim the same stock. Returns false if it cannot be met.
        public bool ResolveRecursive(int itemType, int needed, Dictionary<int, int> available,
            List<CoreStep> steps, HashSet<int> resolving, int depth)
        {
            if (depth > MaxDepth)
                return false;

            if (available.TryGetValue(itemType, out int have) && have >= needed)
            {
                available[itemType] -= needed;
                return true;
            }

            int deficit = needed;
            if (have > 0)
            {
                deficit -= have;
                available[itemType] = 0;
            }

            if (!resolving.Add(itemType))
                return false;

            var recipes = _env.RecipesProducing(itemType);

            IEnumerable<CoreRecipe> ordered = recipes;
            if (recipes.Count > 1)
                ordered = recipes.OrderByDescending(r => StationsAllSatisfied(r));

            List<CoreStep> fallbackSteps = null;
            Dictionary<int, int> fallbackAvailable = null;

            foreach (var recipe in ordered)
            {
                if (!_env.ConditionsMet(recipe))
                    continue;

                int stepsBefore = steps.Count;
                var availSnapshot = new Dictionary<int, int>(available);

                if (!TryResolveRecipe(recipe, itemType, deficit, available, steps, resolving, depth))
                    continue;

                bool stationsComplete = true;
                for (int si = stepsBefore; si < steps.Count && stationsComplete; si++)
                    foreach (int st in steps[si].RequiredStations)
                        if (!_env.IsStationSatisfied(st)) { stationsComplete = false; break; }

                if (stationsComplete)
                {
                    resolving.Remove(itemType);
                    return true;
                }

                if (fallbackSteps == null)
                {
                    fallbackSteps = steps.GetRange(stepsBefore, steps.Count - stepsBefore);
                    fallbackAvailable = new Dictionary<int, int>(available);
                }
                steps.RemoveRange(stepsBefore, steps.Count - stepsBefore);
                available.Clear();
                foreach (var kvp in availSnapshot)
                    available[kvp.Key] = kvp.Value;
            }

            if (fallbackSteps != null)
            {
                steps.AddRange(fallbackSteps);
                available.Clear();
                foreach (var kvp in fallbackAvailable)
                    available[kvp.Key] = kvp.Value;
                resolving.Remove(itemType);
                return true;
            }

            resolving.Remove(itemType);
            return false;
        }

        // Satisfies `deficit` units of `itemType` via one specific recipe. On success appends the
        // sub-steps and this recipe's step, credits overproduction back to the pool, returns true.
        // On ingredient failure rolls the pool back and returns false. Caller owns the `resolving`
        // entry for itemType and the per-recipe condition check.
        public bool TryResolveRecipe(CoreRecipe recipe, int itemType, int deficit,
            Dictionary<int, int> available, List<CoreStep> steps, HashSet<int> resolving, int depth)
        {
            var stepStations = new List<int>(recipe.RequiredTiles);

            int craftsNeeded = (int)Math.Ceiling((double)deficit / recipe.OutputStack);

            var availBackup = new Dictionary<int, int>(available);
            var tempSteps = new List<CoreStep>();
            var consumed = new Dictionary<int, int>();

            foreach (var ingredient in recipe.Ingredients)
            {
                int resolvedType = ResolveIngredientType(recipe, ingredient.Type, available);
                int ingredientNeeded = ingredient.Stack * craftsNeeded;
                consumed[resolvedType] = ingredientNeeded;

                if (!ResolveRecursive(resolvedType, ingredientNeeded, available, tempSteps, resolving, depth + 1))
                {
                    available.Clear();
                    foreach (var kvp in availBackup)
                        available[kvp.Key] = kvp.Value;
                    return false;
                }
            }

            steps.AddRange(tempSteps);

            int produced = craftsNeeded * recipe.OutputStack;
            steps.Add(new CoreStep
            {
                Recipe = recipe,
                CraftCount = craftsNeeded,
                Consumed = consumed,
                ProducedType = itemType,
                ProducedCount = produced,
                RequiredStations = stepStations
            });

            int excess = produced - deficit;
            if (excess > 0)
            {
                if (!available.ContainsKey(itemType))
                    available[itemType] = 0;
                available[itemType] += excess;
            }

            return true;
        }

        // Confirms a recipe by simulating ALL its ingredients against ONE shared, deducting clone of
        // the snapshot — so two ingredients drawing on the same base material cannot each be counted
        // against the full stock. Mirrors the per-recipe block in ResolveRecursive.
        private bool IsRecipeFeasibleShared(CoreRecipe recipe, Dictionary<int, int> availableSnapshot)
        {
            var available = new Dictionary<int, int>(availableSnapshot);
            var steps = new List<CoreStep>();
            var resolving = new HashSet<int> { recipe.OutputType };

            foreach (var ingredient in recipe.Ingredients)
            {
                int resolvedType = ResolveIngredientType(recipe, ingredient.Type, available);
                if (!ResolveRecursive(resolvedType, ingredient.Stack, available, steps, resolving, 1))
                    return false;
            }
            return true;
        }

        // Feasibility of producing `quantity` of `targetItemType` from a cloned snapshot, including
        // a station check on every resulting step. Does not mutate the caller's snapshot.
        public bool IsFeasibleFromSnapshot(int targetItemType, int quantity, Dictionary<int, int> availableSnapshot)
        {
            var available = new Dictionary<int, int>(availableSnapshot);
            var steps = new List<CoreStep>();
            var resolving = new HashSet<int>();
            bool feasible = ResolveRecursive(targetItemType, quantity, available, steps, resolving, 0);
            if (!feasible) return false;

            foreach (var step in steps)
                foreach (int s in step.RequiredStations)
                    if (!_env.IsStationSatisfied(s)) return false;

            return true;
        }

        // Item types transitively producible from current stock (BFS fixpoint, quantities ignored).
        public HashSet<int> ComputeReachableTypes(Dictionary<int, int> available)
        {
            var eligible = new List<CoreRecipe>();
            foreach (var r in _env.AllRecipes)
            {
                bool ok = true;
                foreach (int t in r.RequiredTiles)
                    if (!_env.IsStationSatisfied(t)) { ok = false; break; }
                if (!ok) continue;
                if (!_env.ConditionsMet(r)) continue;
                eligible.Add(r);
            }

            var reachable = new HashSet<int>();
            foreach (var kvp in available)
                if (kvp.Value > 0) reachable.Add(kvp.Key);

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var r in eligible)
                {
                    if (reachable.Contains(r.OutputType)) continue;

                    bool ingredientsMet = true;
                    foreach (var ing in r.Ingredients)
                    {
                        if (reachable.Contains(ing.Type)) continue;

                        bool foundInGroup = false;
                        foreach (int groupId in r.AcceptedGroups)
                        {
                            if (!_env.GroupContains(groupId, ing.Type)) continue;
                            foreach (int valid in _env.GroupValidItems(groupId))
                                if (reachable.Contains(valid)) { foundInGroup = true; break; }
                            if (foundInGroup) break;
                        }
                        if (!foundInGroup) { ingredientsMet = false; break; }
                    }

                    if (ingredientsMet)
                    {
                        reachable.Add(r.OutputType);
                        changed = true;
                    }
                }
            }
            return reachable;
        }

        // Authoritative craftability of one recipe — direct OR recursive. Mirrors the list-flag pass:
        // a cheap per-ingredient pre-filter (memoised in ingCache), then a single shared-pool confirm
        // only when 2+ ingredients could contend for the same base material.
        public bool IsRecipeCraftable(CoreRecipe recipe, HashSet<int> reachable,
            Dictionary<int, int> available, Dictionary<(int type, int stack), bool> ingCache)
        {
            if (!reachable.Contains(recipe.OutputType)) return false;

            foreach (int t in recipe.RequiredTiles)
                if (!_env.IsStationSatisfied(t)) return false;
            if (!_env.ConditionsMet(recipe)) return false;

            bool allDirect = true;
            bool usedGroupSubstitute = false;
            int realIngredients = 0;
            foreach (var ing in recipe.Ingredients)
            {
                realIngredients++;

                if (IngredientSatisfiedDirectly(recipe, ing.Type, ing.Stack, available, out bool viaGroup))
                {
                    if (viaGroup) usedGroupSubstitute = true;
                    continue;
                }

                allDirect = false;

                var key = (ing.Type, ing.Stack);
                if (!ingCache.TryGetValue(key, out bool ok))
                {
                    ok = IsFeasibleFromSnapshot(ing.Type, ing.Stack, available);
                    ingCache[key] = ok;
                }
                if (!ok) return false;
            }

            bool needsSharedConfirm = realIngredients >= 2 && (!allDirect || usedGroupSubstitute);
            if (needsSharedConfirm && !IsRecipeFeasibleShared(recipe, available))
                return false;

            return true;
        }

        // Builds the per-ingredient availability view for the detail preview. `available` is the
        // real storage snapshot (item type -> count). Ingredients are filled from ONE shared,
        // deducting pool — so a base material usable for two slots (a recipe-group substitute, or
        // the same ore behind two sub-crafts) is not counted twice. TotalHave therefore reflects
        // stock actually claimable for that slot, capped at its need; it is never inflated by what
        // could be sub-crafted (recursive craftability is signalled by HasRecipe instead). This
        // mirrors the resolver's shared-pool accounting, so the preview cannot show an ingredient as
        // satisfied when the recipe as a whole is not craftable.
        public List<IngredientView> ComputeIngredientPreview(CoreRecipe recipe, Dictionary<int, int> available, int craftAmount)
        {
            var views = new List<IngredientView>();
            var seen = new HashSet<int>();
            var pool = new Dictionary<int, int>(available);

            foreach (var ingredient in recipe.Ingredients)
            {
                if (!seen.Add(ingredient.Type)) continue;

                int needed = ingredient.Stack * craftAmount;
                bool hasRecipe = _env.RecipesProducing(ingredient.Type).Count > 0;

                bool isGroup = false;
                foreach (int gid in recipe.AcceptedGroups)
                {
                    if (_env.GroupContains(gid, ingredient.Type)) { isGroup = true; break; }
                }

                // Draw this slot's need from the shared pool: own type first, then group substitutes.
                int have = 0;
                have += DrawFromPool(pool, ingredient.Type, needed - have);
                if (have < needed)
                {
                    foreach (int gid in recipe.AcceptedGroups)
                    {
                        if (!_env.GroupContains(gid, ingredient.Type)) continue;
                        foreach (int v in _env.GroupValidItems(gid))
                        {
                            if (v == ingredient.Type) continue;
                            have += DrawFromPool(pool, v, needed - have);
                            if (have >= needed) break;
                        }
                        break;
                    }
                }

                views.Add(new IngredientView
                {
                    Type = ingredient.Type,
                    TotalHave = have,
                    Needed = needed,
                    HasRecipe = hasRecipe,
                    IsGroup = isGroup
                });
            }

            return views;
        }

        // Takes up to `want` units of `type` from the pool, deducting what it takes. Returns the
        // amount taken (0 if `want` <= 0 or none available).
        private static int DrawFromPool(Dictionary<int, int> pool, int type, int want)
        {
            if (want <= 0) return 0;
            if (!pool.TryGetValue(type, out int have) || have <= 0) return 0;
            int take = Math.Min(want, have);
            pool[type] = have - take;
            return take;
        }
    }
}

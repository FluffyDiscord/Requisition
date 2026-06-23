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

        // Reusable scratch for the allocation-free feasibility path (CanProduce). Each feasibility
        // query mutates the caller's snapshot and rolls it back fully before returning, recording
        // every change here — so one instance is reused across all recipes in a pass instead of
        // cloning a dictionary (and building throwaway step lists) on every recursive call.
        private readonly List<(int type, int amount)> _undo = new();
        private readonly HashSet<int> _feasibilityResolving = new();

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

        // Confirms a recipe by simulating ALL its ingredients against ONE shared, deducting pool — so
        // two ingredients drawing on the same base material cannot each be counted against the full
        // stock. Allocation-free: deducts directly from the caller's snapshot and rolls it back before
        // returning, instead of cloning it and building a throwaway step list.
        private bool IsRecipeFeasibleShared(CoreRecipe recipe, Dictionary<int, int> availableSnapshot)
        {
            int mark = _undo.Count;
            _feasibilityResolving.Clear();
            _feasibilityResolving.Add(recipe.OutputType);

            bool ok = true;
            foreach (var ingredient in recipe.Ingredients)
            {
                int resolvedType = ResolveIngredientType(recipe, ingredient.Type, availableSnapshot);
                if (!CanProduce(resolvedType, ingredient.Stack, availableSnapshot))
                {
                    ok = false;
                    break;
                }
            }

            Rollback(availableSnapshot, mark);
            return ok;
        }

        // Feasibility of producing `quantity` of `targetItemType`. Allocation-free: deducts directly
        // from the caller's snapshot and rolls it back before returning (no clone, no step list).
        // CanProduce only ever uses station-satisfied recipes, so a feasible result is automatically
        // fully station-satisfied — no separate post-check needed.
        public bool IsFeasibleFromSnapshot(int targetItemType, int quantity, Dictionary<int, int> availableSnapshot)
        {
            int mark = _undo.Count;
            _feasibilityResolving.Clear();
            bool ok = CanProduce(targetItemType, quantity, availableSnapshot);
            Rollback(availableSnapshot, mark);
            return ok;
        }

        // Allocation-free recursive feasibility: can `needed` units of `itemType` be obtained from
        // `avail` (directly, or by sub-crafting through station-satisfied recipes)? Deducts what it
        // consumes from `avail` and records every change in _undo so a caller can roll back to a mark.
        // Returns true/false only — no steps. Mirrors ResolveRecursive's feasibility decisions
        // (cycle guard, deficit handling, overproduction credit) with zero allocation.
        private bool CanProduce(int itemType, int needed, Dictionary<int, int> avail)
        {
            avail.TryGetValue(itemType, out int have);
            if (have >= needed)
            {
                avail[itemType] = have - needed;
                _undo.Add((itemType, needed));
                return true;
            }

            int deficit = needed;
            if (have > 0)
            {
                avail[itemType] = 0;
                _undo.Add((itemType, have));
                deficit -= have;
            }

            if (!_feasibilityResolving.Add(itemType))
                return false; // cycle

            foreach (var recipe in _env.RecipesProducing(itemType))
            {
                if (!StationsAllSatisfied(recipe)) continue;   // feasibility requires available stations
                if (!_env.ConditionsMet(recipe)) continue;

                int mark = _undo.Count;
                int craftsNeeded = (int)Math.Ceiling((double)deficit / recipe.OutputStack);

                bool ok = true;
                foreach (var ingredient in recipe.Ingredients)
                {
                    int resolvedType = ResolveIngredientType(recipe, ingredient.Type, avail);
                    if (!CanProduce(resolvedType, ingredient.Stack * craftsNeeded, avail))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    int excess = craftsNeeded * recipe.OutputStack - deficit;
                    if (excess > 0)
                    {
                        avail.TryGetValue(itemType, out int cur);
                        avail[itemType] = cur + excess;
                        _undo.Add((itemType, -excess)); // rollback subtracts the credited overproduction
                    }
                    _feasibilityResolving.Remove(itemType);
                    return true;
                }

                Rollback(avail, mark); // this recipe failed — undo its sub-deductions and try the next
            }

            _feasibilityResolving.Remove(itemType);
            return false;
        }

        // Restores `avail` by replaying _undo back to `mark` (each entry's amount is added back:
        // positive un-deducts a consumption, negative un-credits an overproduction), then trims the log.
        private void Rollback(Dictionary<int, int> avail, int mark)
        {
            for (int i = _undo.Count - 1; i >= mark; i--)
            {
                var (type, amount) = _undo[i];
                avail.TryGetValue(type, out int cur);
                avail[type] = cur + amount;
            }
            _undo.RemoveRange(mark, _undo.Count - mark);
        }

        // Item types transitively producible from current stock (least fixpoint, quantities ignored).
        //
        // Worklist propagation, not a re-scan-everything fixpoint: each recipe's unmet ingredient
        // slots register a reverse edge from every item type that could fill them (own type plus
        // recipe-group substitutes). When a type becomes reachable, only the slots waiting on it are
        // revisited; a recipe's output is published the moment its last slot is filled. Each ingredient
        // edge is processed at most once, so the cost is linear in the number of edges rather than the
        // naive O(passes × recipes) — which degraded to quadratic on long dependency chains (one new
        // item per pass, every pass re-scanning every recipe) and was the source of the open-terminal hitch.
        public HashSet<int> ComputeReachableTypes(Dictionary<int, int> available)
        {
            // Station/condition gate — only these recipes can ever contribute.
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

            int n = eligible.Count;
            var remaining = new int[n];                       // unmet ingredient slots per recipe (-1 = retired)
            var slotSatisfied = new bool[n][];                // per recipe, which slots are already met
            var triggers = new Dictionary<int, List<(int recipe, int slot)>>();
            var queue = new Queue<int>();

            for (int ri = 0; ri < n; ri++)
            {
                var recipe = eligible[ri];

                // Output already in stock: downstream recipes see it via the seed set, and there is
                // nothing to derive — skip building its edges.
                if (reachable.Contains(recipe.OutputType))
                {
                    remaining[ri] = -1;
                    slotSatisfied[ri] = Array.Empty<bool>();
                    continue;
                }

                var ings = recipe.Ingredients;
                var sat = new bool[ings.Count];
                slotSatisfied[ri] = sat;
                int unmet = 0;

                for (int si = 0; si < ings.Count; si++)
                {
                    int type = ings[si].Type;
                    if (SlotMetBySeed(recipe, type, reachable))
                    {
                        sat[si] = true;
                        continue;
                    }

                    unmet++;
                    AddTrigger(triggers, type, ri, si);
                    foreach (int gid in recipe.AcceptedGroups)
                    {
                        if (!_env.GroupContains(gid, type)) continue;
                        foreach (int v in _env.GroupValidItems(gid))
                            AddTrigger(triggers, v, ri, si);
                    }
                }

                remaining[ri] = unmet;
                if (unmet == 0 && reachable.Add(recipe.OutputType))
                    queue.Enqueue(recipe.OutputType);
            }

            while (queue.Count > 0)
            {
                int type = queue.Dequeue();
                if (!triggers.TryGetValue(type, out var slots)) continue;
                foreach (var (ri, si) in slots)
                {
                    if (remaining[ri] <= 0) continue;          // recipe complete or retired
                    var sat = slotSatisfied[ri];
                    if (sat[si]) continue;                     // slot already filled by another type
                    sat[si] = true;
                    if (--remaining[ri] == 0 && reachable.Add(eligible[ri].OutputType))
                        queue.Enqueue(eligible[ri].OutputType);
                }
            }

            return reachable;
        }

        // True if ingredient `type` is satisfied by the seed reachable set — directly or via a
        // recipe-group substitute this recipe accepts. Mirrors the per-ingredient test below.
        private bool SlotMetBySeed(CoreRecipe recipe, int type, HashSet<int> reachable)
        {
            if (reachable.Contains(type)) return true;
            foreach (int gid in recipe.AcceptedGroups)
            {
                if (!_env.GroupContains(gid, type)) continue;
                foreach (int v in _env.GroupValidItems(gid))
                    if (reachable.Contains(v)) return true;
            }
            return false;
        }

        private static void AddTrigger(Dictionary<int, List<(int recipe, int slot)>> triggers, int type, int ri, int si)
        {
            if (!triggers.TryGetValue(type, out var list))
            {
                list = new List<(int recipe, int slot)>();
                triggers[type] = list;
            }
            list.Add((ri, si));
        }

        // Authoritative craftability of one recipe — direct OR recursive. Mirrors the list-flag pass:
        // a cheap per-ingredient pre-filter (memoised in ingCache), then a single shared-pool confirm
        // only when 2+ ingredients could contend for the same base material.
        public bool IsRecipeCraftable(CoreRecipe recipe, HashSet<int> reachable,
            Dictionary<int, int> available, Dictionary<(int type, int stack), bool> ingCache)
        {
            // Fast reject using the precomputed reachable set — worthwhile when sweeping ALL recipes.
            if (!reachable.Contains(recipe.OutputType)) return false;
            return RecheckRecipeCraftable(recipe, available, ingCache);
        }

        // Authoritative craftability of one recipe (direct OR recursive) WITHOUT the reachable
        // pre-filter. For targeted revalidation of a small set of recipes after a storage change,
        // where iterating all recipes (so the reachable fast-reject would pay off) is unnecessary.
        // Result is identical to IsRecipeCraftable: a recipe whose output is not reachable is not
        // craftable, so the omitted fast-reject only changes speed, never the answer.
        public bool RecheckRecipeCraftable(CoreRecipe recipe,
            Dictionary<int, int> available, Dictionary<(int type, int stack), bool> ingCache)
        {
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

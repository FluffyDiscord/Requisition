using System;
using System.Collections.Generic;
using System.Linq;
using TerraStorage.Helpers.Resolver;

namespace TerraStorage.Tests
{
    // Zero-dependency console test runner for the crafting resolver core. Run with:
    //   dotnet run --project Tests
    // Exit code 0 = all green, 1 = a failure (so it can gate a build).
    //
    // Each scenario contrasts the resolver's authoritative feasibility, the list-flag craftability,
    // and BOTH the OLD per-ingredient preview (BuggyPreview — independent checks that double-count
    // shared stock) and the NEW shared-pool preview (CoreResolver.ComputeIngredientPreview). The
    // bug the user reported is that the preview shows materials as available when the recipe is not
    // actually craftable; the BuggyPreview asserts reproduce that, the ComputeIngredientPreview
    // asserts prove the fix.
    public static class Program
    {
        private static int _pass;
        private static int _fail;
        private static readonly List<string> _failures = new();

        public static int Main()
        {
            Console.WriteLine("=== Crafting resolver core tests ===\n");

            SharedBaseContention();
            DemoniteCrimtaneTwoWay();
            RecipeGroupContention();
            StationShadowingSubCraft();
            StationFallbackOrderIndependent();
            DirectAndSimpleSanity();

            Console.WriteLine($"\n=== {_pass} passed, {_fail} failed ===");
            if (_fail > 0)
            {
                Console.WriteLine("\nFAILURES:");
                foreach (var f in _failures) Console.WriteLine("  - " + f);
            }
            return _fail == 0 ? 0 : 1;
        }

        // ---- Scenario 1: two ingredients made from the SAME base ore, only enough ore for one ----
        // This is the canonical "magically get more items" bug: each ingredient checked alone sees
        // the full ore stock, so both report available; the recipe as a whole cannot be crafted.
        private static void SharedBaseContention()
        {
            Section("Shared-base sibling contention (A + B both from ORE_X)");
            const int ORE_X = 10, A = 11, B = 12, TARGET = 13, FURNACE = 100;

            var env = new FakeEnvironment().WithStations(FURNACE);
            env.AddRecipe(A, 1, new[] { (ORE_X, 1) }, tiles: new[] { FURNACE });
            env.AddRecipe(B, 1, new[] { (ORE_X, 1) }, tiles: new[] { FURNACE });
            var target = env.AddRecipe(TARGET, 1, new[] { (A, 1), (B, 1) }, tiles: new[] { FURNACE });
            var r = new CoreResolver(env);

            // Only 1 ore: cannot make both A and B.
            var one = new Dictionary<int, int> { [ORE_X] = 1 };
            IsFalse(r.IsFeasibleFromSnapshot(TARGET, 1, one), "S1 resolver: TARGET NOT craftable with 1 ORE_X");
            IsFalse(ListCraftable(r, target, one), "S1 list-flag: TARGET NOT craftable with 1 ORE_X");

            var buggy = BuggyPreview(r, env, target, one, 1);
            Eq(View(buggy, A).TotalHave, 1, "S1 OLD preview LIES: A shows 1/1 (inflated)");
            Eq(View(buggy, B).TotalHave, 1, "S1 OLD preview LIES: B shows 1/1 (inflated)");

            var fixedView = r.ComputeIngredientPreview(target, one, 1);
            Eq(View(fixedView, A).TotalHave, 0, "S1 NEW preview HONEST: A shows 0/1");
            Eq(View(fixedView, B).TotalHave, 0, "S1 NEW preview HONEST: B shows 0/1");

            // Two ore: genuinely craftable.
            var two = new Dictionary<int, int> { [ORE_X] = 2 };
            IsTrue(r.IsFeasibleFromSnapshot(TARGET, 1, two), "S1 resolver: TARGET craftable with 2 ORE_X");
            IsTrue(ListCraftable(r, target, two), "S1 list-flag: TARGET craftable with 2 ORE_X");
        }

        // ---- Scenario 2: demonite <-> crimtane two-way conversion (single ingredient, cycle) ----
        private static void DemoniteCrimtaneTwoWay()
        {
            Section("Demonite/Crimtane two-way conversion");
            const int DEMO_ORE = 20, DEMO = 21, CRIM = 22, FURNACE = 100;

            var env = new FakeEnvironment().WithStations(FURNACE);
            env.AddRecipe(DEMO, 1, new[] { (DEMO_ORE, 3) }, tiles: new[] { FURNACE });
            var crimRecipe = env.AddRecipe(CRIM, 1, new[] { (DEMO, 1) }, tiles: new[] { FURNACE });
            env.AddRecipe(DEMO, 1, new[] { (CRIM, 1) }, tiles: new[] { FURNACE }); // reverse two-way
            var r = new CoreResolver(env);

            // 2 demonite bars, want 3 crimtane: cannot make the 3rd demonite (no ore; reverse is a cycle).
            var twoBars = new Dictionary<int, int> { [DEMO] = 2 };
            IsFalse(r.IsFeasibleFromSnapshot(CRIM, 3, twoBars), "S2 resolver: 3 CRIM NOT craftable from 2 DEMO (cycle blocked)");

            var buggy = BuggyPreview(r, env, crimRecipe, twoBars, 3);
            Eq(View(buggy, DEMO).TotalHave, 2, "S2 OLD preview: DEMO 2/3 (two-way correctly not inflated)");
            var fixedView = r.ComputeIngredientPreview(crimRecipe, twoBars, 3);
            Eq(View(fixedView, DEMO).TotalHave, 2, "S2 NEW preview: DEMO 2/3 (honest)");

            // 6 ore -> 2 demonite -> 2 crimtane: feasible.
            var sixOre = new Dictionary<int, int> { [DEMO_ORE] = 6 };
            IsTrue(r.IsFeasibleFromSnapshot(CRIM, 2, sixOre), "S2 resolver: 2 CRIM craftable from 6 DEMO_ORE");
            // 5 ore is not enough for 2 demonite (needs 6).
            var fiveOre = new Dictionary<int, int> { [DEMO_ORE] = 5 };
            IsFalse(r.IsFeasibleFromSnapshot(CRIM, 2, fiveOre), "S2 resolver: 2 CRIM NOT craftable from 5 DEMO_ORE");
        }

        // ---- Scenario 3: recipe-group contention (copper/tin bars share a group) ----
        // The preview's group-sum double-counts a single shared bar across two slots even without
        // the recursive inflation — the deducting pool is what fixes it.
        private static void RecipeGroupContention()
        {
            Section("Recipe-group contention (copper/tin shared group)");
            const int COPPER = 31, TIN = 32, WIDGET = 34, BAR_GROUP = 1000;

            var env = new FakeEnvironment().WithGroup(BAR_GROUP, COPPER, TIN);
            // Both slots accept the group, so either bar can fill either slot.
            var widget = env.AddRecipe(WIDGET, 1, new[] { (COPPER, 1), (TIN, 1) }, groups: new[] { BAR_GROUP });
            var r = new CoreResolver(env);

            var oneCopper = new Dictionary<int, int> { [COPPER] = 1 };
            IsFalse(r.IsFeasibleFromSnapshot(WIDGET, 1, oneCopper), "S3 resolver: WIDGET NOT craftable from 1 bar");
            IsFalse(ListCraftable(r, widget, oneCopper), "S3 list-flag: WIDGET NOT craftable from 1 bar");

            var buggy = BuggyPreview(r, env, widget, oneCopper, 1);
            Eq(View(buggy, TIN).TotalHave, 1, "S3 OLD preview LIES: TIN slot shows 1/1 (counts the copper twice)");

            var fixedView = r.ComputeIngredientPreview(widget, oneCopper, 1);
            Eq(View(fixedView, COPPER).TotalHave, 1, "S3 NEW preview: COPPER slot 1/1 (claims the one bar)");
            Eq(View(fixedView, TIN).TotalHave, 0, "S3 NEW preview HONEST: TIN slot 0/1 (bar already claimed)");

            var copperAndTin = new Dictionary<int, int> { [COPPER] = 1, [TIN] = 1 };
            IsTrue(r.IsFeasibleFromSnapshot(WIDGET, 1, copperAndTin), "S3 resolver: WIDGET craftable from 1 copper + 1 tin");
            var bothView = r.ComputeIngredientPreview(widget, copperAndTin, 1);
            Eq(View(bothView, COPPER).TotalHave, 1, "S3 NEW preview: COPPER 1/1 with both bars");
            Eq(View(bothView, TIN).TotalHave, 1, "S3 NEW preview: TIN 1/1 with both bars");
        }

        // ---- Scenario 4: a recipe whose alternative path sub-crafts at a missing station ----
        // The station-feasible conversion must win over a path that needs a station you don't have,
        // in DEFAULT mode (no lock). Regression guard for the "missing stations" fix.
        private static void StationShadowingSubCraft()
        {
            Section("Station shadowing: pick the fully-station-feasible recipe");
            const int DEMO = 21, CRIM = 22, HELL_BAR = 40, HELL_ORE = 41, FURNACE = 100, HELLFORGE = 200;

            var env = new FakeEnvironment().WithStations(FURNACE); // no hellforge
            // Path 1: CRIM from DEMO at a furnace (we have the furnace).
            env.AddRecipe(CRIM, 1, new[] { (DEMO, 1) }, tiles: new[] { FURNACE });
            // Path 2: CRIM from a hell bar, which is sub-crafted at a hellforge we DON'T have.
            env.AddRecipe(CRIM, 1, new[] { (HELL_BAR, 1) }, tiles: new[] { FURNACE });
            env.AddRecipe(HELL_BAR, 1, new[] { (HELL_ORE, 1) }, tiles: new[] { HELLFORGE });
            var r = new CoreResolver(env);

            // Have demonite AND hell ore, but no hellforge: must resolve via the demonite path.
            var stock = new Dictionary<int, int> { [DEMO] = 5, [HELL_ORE] = 5 };
            IsTrue(r.IsFeasibleFromSnapshot(CRIM, 1, stock), "S4 resolver: CRIM craftable via furnace path (default mode)");

            // Sanity: with neither furnace path material nor hellforge, it is genuinely infeasible.
            var onlyHellOre = new Dictionary<int, int> { [HELL_ORE] = 5 };
            IsFalse(r.IsFeasibleFromSnapshot(CRIM, 1, onlyHellOre), "S4 resolver: CRIM infeasible when only the hellforge path exists");
        }

        // ---- Scenario 4b: the same, but the station-BLOCKED recipe is registered FIRST ----
        // Exercises the fallback/rollback path itself (not just the top-level ordering heuristic):
        // the resolver must reject the hellforge sub-craft, roll back, and resolve via the furnace path.
        private static void StationFallbackOrderIndependent()
        {
            Section("Station fallback triggers even when the blocked recipe is first");
            const int DEMO = 21, CRIM = 22, HELL_BAR = 40, HELL_ORE = 41, FURNACE = 100, HELLFORGE = 200;

            var env = new FakeEnvironment().WithStations(FURNACE); // no hellforge
            // Blocked path FIRST: CRIM <- HELL_BAR, and HELL_BAR needs a hellforge we don't have.
            env.AddRecipe(CRIM, 1, new[] { (HELL_BAR, 1) }, tiles: new[] { FURNACE });
            env.AddRecipe(HELL_BAR, 1, new[] { (HELL_ORE, 1) }, tiles: new[] { HELLFORGE });
            // Good path registered LAST: CRIM <- DEMO at a furnace.
            env.AddRecipe(CRIM, 1, new[] { (DEMO, 1) }, tiles: new[] { FURNACE });
            var r = new CoreResolver(env);

            var stock = new Dictionary<int, int> { [DEMO] = 5, [HELL_ORE] = 5 };
            IsTrue(r.IsFeasibleFromSnapshot(CRIM, 1, stock),
                "S4b resolver: CRIM resolves via furnace path even though the hellforge path is registered first");
        }

        // ---- Scenario 5: plain sanity (direct have, simple shortfall) ----
        private static void DirectAndSimpleSanity()
        {
            Section("Direct/simple sanity");
            const int ORE = 50, BAR = 51, FURNACE = 100;

            var env = new FakeEnvironment().WithStations(FURNACE);
            var barRecipe = env.AddRecipe(BAR, 1, new[] { (ORE, 3) }, tiles: new[] { FURNACE });
            var r = new CoreResolver(env);

            var threeOre = new Dictionary<int, int> { [ORE] = 3 };
            IsTrue(r.IsFeasibleFromSnapshot(BAR, 1, threeOre), "S5 resolver: 1 BAR craftable from 3 ORE");
            Eq(View(r.ComputeIngredientPreview(barRecipe, threeOre, 1), ORE).TotalHave, 3, "S5 preview: ORE 3/3");

            var twoOre = new Dictionary<int, int> { [ORE] = 2 };
            IsFalse(r.IsFeasibleFromSnapshot(BAR, 1, twoOre), "S5 resolver: 1 BAR NOT craftable from 2 ORE");
            Eq(View(r.ComputeIngredientPreview(barRecipe, twoOre, 1), ORE).TotalHave, 2, "S5 preview: ORE 2/3 (honest shortfall)");

            // craftAmount scales need.
            var sixOre = new Dictionary<int, int> { [ORE] = 6 };
            IsTrue(r.IsFeasibleFromSnapshot(BAR, 2, sixOre), "S5 resolver: 2 BAR craftable from 6 ORE");
            Eq(View(r.ComputeIngredientPreview(barRecipe, sixOre, 2), ORE).Needed, 6, "S5 preview: need scales with craftAmount (6)");
        }

        // ---- The OLD preview logic, reproduced verbatim, to prove what was wrong ----
        // Each ingredient is checked INDEPENDENTLY against the full stock: group members are summed
        // and a recursive sub-plan is resolved against an un-deducted snapshot, so two ingredients
        // sharing a base both report it as theirs.
        private static List<IngredientView> BuggyPreview(CoreResolver r, IRecipeEnvironment env,
            CoreRecipe recipe, Dictionary<int, int> available, int craftAmount)
        {
            var views = new List<IngredientView>();
            var seen = new HashSet<int>();
            foreach (var ing in recipe.Ingredients)
            {
                if (!seen.Add(ing.Type)) continue;

                available.TryGetValue(ing.Type, out int directHave);
                bool hasRecipe = env.RecipesProducing(ing.Type).Count > 0;

                bool isGroup = false;
                int totalHave = directHave;
                foreach (int gid in recipe.AcceptedGroups)
                {
                    if (!env.GroupContains(gid, ing.Type)) continue;
                    isGroup = true;
                    foreach (int v in env.GroupValidItems(gid))
                        if (v != ing.Type && available.TryGetValue(v, out int vh))
                            totalHave += vh;
                    break;
                }

                int needed = ing.Stack * craftAmount;
                if (hasRecipe && totalHave < needed)
                {
                    if (r.IsFeasibleFromSnapshot(ing.Type, needed, available))
                        totalHave = needed; // the inflation
                }

                views.Add(new IngredientView { Type = ing.Type, TotalHave = totalHave, Needed = needed, HasRecipe = hasRecipe, IsGroup = isGroup });
            }
            return views;
        }

        private static bool ListCraftable(CoreResolver r, CoreRecipe recipe, Dictionary<int, int> available)
        {
            var reachable = r.ComputeReachableTypes(available);
            var ingCache = new Dictionary<(int type, int stack), bool>();
            return r.IsRecipeCraftable(recipe, reachable, available, ingCache);
        }

        private static IngredientView View(List<IngredientView> views, int type) => views.First(v => v.Type == type);

        // ---- tiny assert framework ----
        private static void Section(string title) => Console.WriteLine($"-- {title}");

        private static void Check(bool cond, string name)
        {
            if (cond) { _pass++; Console.WriteLine($"   PASS  {name}"); }
            else { _fail++; _failures.Add(name); Console.WriteLine($"   FAIL  {name}"); }
        }

        private static void Eq(int actual, int expected, string name) => Check(actual == expected, $"{name}  [expected {expected}, got {actual}]");
        private static void IsTrue(bool c, string n) => Check(c, n);
        private static void IsFalse(bool c, string n) => Check(!c, n);
    }
}

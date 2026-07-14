using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TerraStorage.Content.UI;
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
            ReachabilityEquivalence();
            ReachabilityScaleBenchmark();
            ReachabilityRealisticScaleBenchmark();
            RealDumpBenchmark();

            WindowStackTests();
            DepositGateTests();
            ClickBlockerTests();

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

        // ---- Reachability: the optimized worklist must equal the old naive fixpoint exactly ----
        // ComputeReachableTypes was rewritten from a re-scan-every-recipe-every-pass fixpoint
        // (quadratic on dependency chains — the open-terminal freeze) to worklist propagation.
        // ReachableNaive below is the OLD algorithm verbatim, used as the oracle.
        private static void ReachabilityEquivalence()
        {
            Section("Reachability: worklist == naive fixpoint");

            // Shaped case 1: linear chain 0->1->...->K from seed {0} (the pathological O(R^2) shape).
            {
                var env = new FakeEnvironment();
                for (int i = 0; i < 50; i++) env.AddRecipe(i + 1, 1, new[] { (i, 1) });
                AssertReachableEquiv(env, new Dictionary<int, int> { [0] = 1 }, "linear chain K=50");
            }

            // Shaped case 2: a cycle, a recipe-group ingredient, and a station we lack (never reachable).
            {
                var env = new FakeEnvironment().WithStations(100).WithGroup(1000, 200, 201);
                env.AddRecipe(2, 1, new[] { (0, 1) }, tiles: new[] { 100 });
                env.AddRecipe(0, 1, new[] { (2, 1) }, tiles: new[] { 100 });        // cycle 0<->2
                env.AddRecipe(3, 1, new[] { (200, 1) }, groups: new[] { 1000 });    // satisfiable via 201
                env.AddRecipe(4, 1, new[] { (2, 1), (3, 1) }, tiles: new[] { 100 });
                env.AddRecipe(5, 1, new[] { (4, 1) }, tiles: new[] { 999 });        // station 999 missing
                AssertReachableEquiv(env, new Dictionary<int, int> { [0] = 5, [201] = 1 }, "cycle+group+missing-station");
            }

            // Randomized worlds: cycles, self-loops, groups, unsatisfiable stations, multi-ingredient.
            var rng = new Random(0xC0FFEE);
            bool allEq = true;
            int firstFail = -1;
            for (int trial = 0; trial < 400 && allEq; trial++)
            {
                var env = BuildRandomEnv(rng, out var available);
                var core = new CoreResolver(env);
                if (!core.ComputeReachableTypes(available).SetEquals(ReachableNaive(env, available)))
                {
                    allEq = false;
                    firstFail = trial;
                }
            }
            Check(allEq, $"reachable equivalence over 400 random worlds (first mismatch: {firstFail})");
        }

        // Proves the rewrite is not just correct but materially faster on the shape that caused the
        // freeze: a long dependency chain, where the naive fixpoint does one pass per link.
        private static void ReachabilityScaleBenchmark()
        {
            Section("Reachability scale benchmark (long dependency chain)");
            const int K = 5000;
            var env = new FakeEnvironment();
            // Register in REVERSE dependency order (produces K first, ..., produces 1 last). This is the
            // naive fixpoint's worst case — one new item per full pass, K passes over K recipes = O(K^2) —
            // and it mirrors reality: mods register recipes in load order, not dependency order.
            for (int i = K - 1; i >= 0; i--) env.AddRecipe(i + 1, 1, new[] { (i, 1) });
            var available = new Dictionary<int, int> { [0] = 1 };
            var core = new CoreResolver(env);

            // Warm + measure the worklist.
            var fast = core.ComputeReachableTypes(available);
            var sw = Stopwatch.StartNew();
            fast = core.ComputeReachableTypes(available);
            sw.Stop();
            long fastMs = sw.ElapsedMilliseconds;

            sw.Restart();
            var naive = ReachableNaive(env, available);
            sw.Stop();
            long naiveMs = sw.ElapsedMilliseconds;

            Check(fast.SetEquals(naive), $"scale: worklist == naive on K={K} chain");
            Check(fast.Count == K + 1, $"scale: all {K + 1} types reachable (got {fast.Count})");
            string ratio = fastMs > 0 ? $"x{(double)naiveMs / fastMs:0.0}" : ">>";
            Console.WriteLine($"   TIME  worklist={fastMs}ms  naive={naiveMs}ms  speedup {ratio}  (K={K} recipes)");
            // Generous bound (the real gap is orders of magnitude); guards against a regression to quadratic.
            Check(fastMs * 4 < naiveMs || naiveMs < 4, $"scale: worklist materially faster (fast={fastMs}ms, naive={naiveMs}ms)");
        }

        // Wall-time of ComputeReachableTypes on a realistic heavy-modpack-scale branching graph,
        // to decide whether the (currently single-frame) call needs to be spread across frames.
        private static void ReachabilityRealisticScaleBenchmark()
        {
            Section("Reachability realistic-scale wall time (branching DAG)");
            const int Recipes = 20000;
            const int BaseItems = 400;
            var env = new FakeEnvironment().WithStations(100).WithGroup(1000, 0, 1, 2, 3, 4);
            var rng = new Random(99);
            for (int i = 0; i < Recipes; i++)
            {
                int outType = BaseItems + i;
                int ingCount = rng.Next(1, 5);
                var ings = new (int, int)[ingCount];
                for (int j = 0; j < ingCount; j++) ings[j] = (rng.Next(BaseItems + i), rng.Next(1, 4)); // depends on earlier items
                int[] tiles = rng.Next(4) == 0 ? null : new[] { 100 };
                int[] groups = rng.Next(6) == 0 ? new[] { 1000 } : null;
                env.AddRecipe(outType, rng.Next(1, 3), ings, tiles, groups);
            }
            var available = new Dictionary<int, int>();
            for (int i = 0; i < 100; i++) available[rng.Next(BaseItems)] = rng.Next(1, 50);

            var core = new CoreResolver(env);
            core.ComputeReachableTypes(available); // warm

            long best = long.MaxValue;
            int count = 0;
            for (int run = 0; run < 5; run++)
            {
                var sw = Stopwatch.StartNew();
                var reachable = core.ComputeReachableTypes(available);
                sw.Stop();
                count = reachable.Count;
                best = Math.Min(best, sw.ElapsedMilliseconds);
            }
            Console.WriteLine($"   TIME  ComputeReachableTypes = {best}ms  ({Recipes} recipes, {count} reachable types, best of 5)");
            // A single frame at 60fps is ~16.7ms. Flag if one call eats more than a third of a frame.
            Check(best <= 6, $"reachability stays well under one frame at {Recipes} recipes ({best}ms)");
        }

        // Real-game data: compares the current FULL revalidation (recompute reachable + re-check
        // every recipe — what commit 506eda8 runs on each storage change) against a TARGETED update
        // (only recipes that use a changed item type as an ingredient, plus the result recipes).
        // Skips silently if no /tsdump file is present, so it never breaks the suite elsewhere.
        private static void RealDumpBenchmark()
        {
            Section("Real-game dump: full vs targeted craftability revalidation");
            string dump = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "My Games", "Terraria", "tModLoader", "ts_recipe_dump.txt");
            if (!File.Exists(dump)) { Console.WriteLine($"   SKIP  no dump at {dump}"); return; }

            var env = new FakeEnvironment();
            var available = new Dictionary<int, int>();
            var stations = new HashSet<int>();
            ParseDump(dump, env, available, stations);
            var recipes = env.AllRecipes;
            Console.WriteLine($"   loaded {recipes.Count} recipes, {available.Count} stored types, {stations.Count} stations");

            var core = new CoreResolver(env) { MaxDepth = 10 };

            // Reverse indices, mirroring the panel's RebuildIngredientIndex (incl. group substitutes).
            var ingIndex = new Dictionary<int, List<int>>();
            var outIndex = new Dictionary<int, List<int>>();
            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                Add(outIndex, r.OutputType, i);
                foreach (var ing in r.Ingredients)
                {
                    Add(ingIndex, ing.Type, i);
                    foreach (int gid in r.AcceptedGroups)
                        if (env.GroupContains(gid, ing.Type))
                            foreach (int v in env.GroupValidItems(gid))
                                if (v != ing.Type) Add(ingIndex, v, i);
                }
            }

            // FULL revalidation (current per-storage-change behavior).
            var sw = Stopwatch.StartNew();
            var reachable = core.ComputeReachableTypes(available);
            long reachMs = sw.ElapsedMilliseconds;
            var canCraft = new bool[recipes.Count];
            var ingCacheFull = new Dictionary<(int, int), bool>();
            for (int i = 0; i < recipes.Count; i++)
                canCraft[i] = core.IsRecipeCraftable(recipes[i], reachable, available, ingCacheFull);
            sw.Stop();
            long fullMs = sw.ElapsedMilliseconds;
            Console.WriteLine($"   FULL revalidation: {fullMs}ms (of which reachable {reachMs}ms), {canCraft.Count(b => b)} craftable");

            // Simulate crafting the first craftable recipe: consume its ingredients, produce its output.
            int craftIdx = -1;
            for (int i = 0; i < recipes.Count; i++)
                if (canCraft[i] && recipes[i].Ingredients.Count >= 1) { craftIdx = i; break; }
            if (craftIdx < 0) { Console.WriteLine("   no craftable recipe to simulate; skipping"); return; }

            var crafted = recipes[craftIdx];
            var after = new Dictionary<int, int>(available);
            var changed = new HashSet<int>();
            foreach (var ing in crafted.Ingredients)
                if (after.TryGetValue(ing.Type, out int h)) { after[ing.Type] = Math.Max(0, h - ing.Stack); changed.Add(ing.Type); }
            after.TryGetValue(crafted.OutputType, out int oh);
            after[crafted.OutputType] = oh + crafted.OutputStack;
            changed.Add(crafted.OutputType);

            // Oracle: a FULL recompute after the craft (the authoritative result every variant is checked against).
            var reachableAfter = core.ComputeReachableTypes(after);
            var fullAfter = new bool[recipes.Count];
            var ingCacheO = new Dictionary<(int, int), bool>();
            for (int i = 0; i < recipes.Count; i++)
                fullAfter[i] = core.IsRecipeCraftable(recipes[i], reachableAfter, after, ingCacheO);

            // Split the change into consumed (decreased) and produced (increased) types.
            var decreased = new HashSet<int>();
            var increased = new HashSet<int>();
            foreach (int t in changed)
            {
                available.TryGetValue(t, out int b4);
                after.TryGetValue(t, out int af);
                if (af < b4) decreased.Add(t);
                else if (af > b4) increased.Add(t);
            }

            // Variant A — every recipe touching a changed type (the broad "contains changed ingredient" set).
            var affectedA = new HashSet<int>();
            foreach (int t in changed)
            {
                if (ingIndex.TryGetValue(t, out var a)) affectedA.UnionWith(a);
                if (outIndex.TryGetValue(t, out var b)) affectedA.UnionWith(b);
            }

            // Variant B — direction-aware: a consumed item can only DEMOTE a currently-craftable recipe;
            // a produced item can only PROMOTE a currently-uncraftable one. Plus the result recipe(s).
            var affectedB = new HashSet<int>();
            foreach (int t in decreased)
                if (ingIndex.TryGetValue(t, out var a)) foreach (int i in a) if (canCraft[i]) affectedB.Add(i);
            foreach (int t in increased)
                if (ingIndex.TryGetValue(t, out var a)) foreach (int i in a) if (!canCraft[i]) affectedB.Add(i);
            foreach (int t in increased)
                if (outIndex.TryGetValue(t, out var b)) affectedB.UnionWith(b);

            long timeReachAfter = MeasureMs(() => core.ComputeReachableTypes(after));
            var (msA, matchA, missA) = MeasureTargeted(core, recipes, affectedA, reachableAfter, after, fullAfter, canCraft, useGate: true);
            var (msB, matchB, missB) = MeasureTargeted(core, recipes, affectedB, reachableAfter, after, fullAfter, canCraft, useGate: true);

            Check(matchA, "real dump: broad targeted (A) matches full recompute for every recipe it re-checks");
            Check(matchB, "real dump: direction-aware targeted (B) matches full recompute for every recipe it re-checks");
            Console.WriteLine($"   crafted recipe #{craftIdx} (output {crafted.OutputType}); changed types: {changed.Count} (consumed {decreased.Count}, produced {increased.Count})");
            Console.WriteLine($"   reachable recompute after craft: {timeReachAfter}ms");
            Console.WriteLine($"   A broad      : {affectedA.Count,5} recipes, {msA,4}ms, {missA} flips missed");
            Console.WriteLine($"   B direction  : {affectedB.Count,5} recipes, {msB,4}ms, {missB} flips missed");
            Console.WriteLine($"   >>> full {fullMs}ms  ->  A {msA + timeReachAfter}ms  ->  B {msB + timeReachAfter}ms (incl. reachable recompute)");

            // --- Memory-vs-compute classification: bytes allocated + GC pressure per pass ---
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long aF = GC.GetAllocatedBytesForCurrentThread();
            int gF = GC.CollectionCount(0);
            var rchF = core.ComputeReachableTypes(available);
            var icF = new Dictionary<(int, int), bool>();
            for (int i = 0; i < recipes.Count; i++) core.IsRecipeCraftable(recipes[i], rchF, available, icF);
            double fullMB = (GC.GetAllocatedBytesForCurrentThread() - aF) / 1048576.0;
            int fullGen0 = GC.CollectionCount(0) - gF;

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long aT = GC.GetAllocatedBytesForCurrentThread();
            int gT = GC.CollectionCount(0);
            var rchT = core.ComputeReachableTypes(after);
            var icT = new Dictionary<(int, int), bool>();
            foreach (int i in affectedB) core.IsRecipeCraftable(recipes[i], rchT, after, icT);
            double targMB = (GC.GetAllocatedBytesForCurrentThread() - aT) / 1048576.0;
            int targGen0 = GC.CollectionCount(0) - gT;

            Console.WriteLine($"   MEM  full     : {fullMB,7:0.0} MB allocated, {fullGen0} gen0 GCs  ({fullMB / Math.Max(1, recipes.Count) * 1024:0.0} KB/recipe)");
            Console.WriteLine($"   MEM  targeted : {targMB,7:0.0} MB allocated, {targGen0} gen0 GCs  ({targMB / Math.Max(1, affectedB.Count) * 1024:0.0} KB/recipe)");
        }

        private static long MeasureMs(Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            sw.Stop();
            return sw.ElapsedMilliseconds;
        }

        private static (long ms, bool match, int miss) MeasureTargeted(
            CoreResolver core, IReadOnlyList<CoreRecipe> recipes, HashSet<int> affected,
            HashSet<int> reachableAfter, Dictionary<int, int> after, bool[] fullAfter, bool[] canCraft, bool useGate)
        {
            var ingCache = new Dictionary<(int, int), bool>();
            var result = new Dictionary<int, bool>();
            var sw = Stopwatch.StartNew();
            foreach (int i in affected)
                result[i] = useGate
                    ? core.IsRecipeCraftable(recipes[i], reachableAfter, after, ingCache)
                    : core.RecheckRecipeCraftable(recipes[i], after, ingCache);
            sw.Stop();

            bool match = affected.All(i => result[i] == fullAfter[i]);
            int miss = 0;
            for (int i = 0; i < recipes.Count; i++)
                if (!affected.Contains(i) && fullAfter[i] != canCraft[i]) miss++;
            return (sw.ElapsedMilliseconds, match, miss);
        }

        private static void Add(Dictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var list)) { list = new List<int>(); map[key] = list; }
            list.Add(value);
        }

        private static void ParseDump(string path, FakeEnvironment env, Dictionary<int, int> available, HashSet<int> stations)
        {
            int section = 0; // 1=storage 2=groups 3=recipes
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("STATIONS:")) { foreach (var s in line.Substring(9).Split(' ', StringSplitOptions.RemoveEmptyEntries)) stations.Add(int.Parse(s)); continue; }
                if (line.StartsWith("CONDITIONS:")) continue; // FakeEnvironment treats conditions as always met
                if (line == "STORAGE:") { section = 1; continue; }
                if (line == "GROUPS:") { section = 2; continue; }
                if (line == "RECIPES:") { section = 3; continue; }

                try
                {
                    if (section == 1)
                    {
                        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        available[int.Parse(p[0])] = int.Parse(p[1]);
                    }
                    else if (section == 2)
                    {
                        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        env.WithGroup(int.Parse(p[0]), p.Skip(1).Select(int.Parse).ToArray());
                    }
                    else if (section == 3)
                    {
                        var parts = line.Split('|');
                        var outp = parts[0].Trim().Split(':');
                        int outType = int.Parse(outp[0]), outStack = int.Parse(outp[1]);
                        var ings = parts[1].Trim().Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => { var ip = s.Split(':'); return (int.Parse(ip[0]), int.Parse(ip[1])); }).ToArray();
                        var tiles = parts[2].Replace("tiles:", "").Trim().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                        var groups = parts[3].Replace("groups:", "").Trim().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                        env.AddRecipe(outType, outStack, ings, tiles.Length > 0 ? tiles : null, groups.Length > 0 ? groups : null);
                    }
                }
                catch { /* skip malformed line */ }
            }
            env.WithStations(stations.ToArray());
        }

        private static void AssertReachableEquiv(IRecipeEnvironment env, Dictionary<int, int> available, string label)
        {
            var fast = new CoreResolver(env).ComputeReachableTypes(available);
            var naive = ReachableNaive(env, available);
            Check(fast.SetEquals(naive), $"reachable equivalence: {label} (worklist={fast.Count}, naive={naive.Count})");
        }

        // A random recipe world: cycles, self-loops, recipe groups, sometimes-unsatisfiable stations.
        private static FakeEnvironment BuildRandomEnv(Random rng, out Dictionary<int, int> available)
        {
            var env = new FakeEnvironment();
            int itemSpace = rng.Next(8, 40);
            bool hasStation = rng.Next(2) == 0;
            if (hasStation) env.WithStations(100);
            bool useGroup = rng.Next(3) == 0;
            const int groupId = 1000;
            if (useGroup) env.WithGroup(groupId, 0, 1);

            int recipeCount = rng.Next(5, 60);
            for (int i = 0; i < recipeCount; i++)
            {
                int outType = rng.Next(itemSpace);
                int ingCount = rng.Next(1, 4);
                var ings = new (int, int)[ingCount];
                for (int j = 0; j < ingCount; j++) ings[j] = (rng.Next(itemSpace), rng.Next(1, 4));

                int[] tiles = null;
                int tileRoll = rng.Next(4);
                if (tileRoll == 0) tiles = new[] { 999 };               // station we never have
                else if (hasStation && tileRoll == 1) tiles = new[] { 100 };

                int[] groups = (useGroup && rng.Next(3) == 0) ? new[] { groupId } : null;
                env.AddRecipe(outType, rng.Next(1, 3), ings, tiles, groups);
            }

            available = new Dictionary<int, int>();
            int seedCount = rng.Next(1, 6);
            for (int i = 0; i < seedCount; i++) available[rng.Next(itemSpace)] = rng.Next(1, 10);
            return env;
        }

        // The OLD ComputeReachableTypes algorithm, verbatim, as the equivalence oracle.
        private static HashSet<int> ReachableNaive(IRecipeEnvironment env, Dictionary<int, int> available)
        {
            var eligible = new List<CoreRecipe>();
            foreach (var r in env.AllRecipes)
            {
                bool ok = true;
                foreach (int t in r.RequiredTiles)
                    if (!env.IsStationSatisfied(t)) { ok = false; break; }
                if (!ok) continue;
                if (!env.ConditionsMet(r)) continue;
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

                    bool met = true;
                    foreach (var ing in r.Ingredients)
                    {
                        if (reachable.Contains(ing.Type)) continue;
                        bool grp = false;
                        foreach (int gid in r.AcceptedGroups)
                        {
                            if (!env.GroupContains(gid, ing.Type)) continue;
                            foreach (int v in env.GroupValidItems(gid))
                                if (reachable.Contains(v)) { grp = true; break; }
                            if (grp) break;
                        }
                        if (!grp) { met = false; break; }
                    }
                    if (met) { reachable.Add(r.OutputType); changed = true; }
                }
            }
            return reachable;
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

        // The window a click belongs to is the topmost one under the cursor. Before this, the
        // window that consumed a click was whichever ModSystem happened to update first, which had
        // nothing to do with what the player saw on top.
        private static void WindowStackTests()
        {
            Section("WindowStackCore - z-order arbitration");

            var stack = new WindowStackCore();
            int a = stack.Register();
            int b = stack.Register();           // registered last, so it sits on top
            Eq(stack.TopMatching(new[] { true, true }), b, "TC-001 both hovered -> topmost wins");

            stack.Raise(a);
            Eq(stack.TopMatching(new[] { true, true }), a, "TC-002 raise(A) -> A now wins");

            var fallThrough = new WindowStackCore();
            int belowA = fallThrough.Register();
            fallThrough.Register();
            Eq(fallThrough.TopMatching(new[] { true, false }), belowA,
                "TC-003 cursor outside the top window -> falls through to the one below");
            Eq(fallThrough.TopMatching(new[] { false, false }), -1, "TC-004 nothing hovered -> -1");

            var withClosed = new WindowStackCore();
            withClosed.Register();
            withClosed.Register();
            int top = withClosed.Register();
            Eq(withClosed.TopMatching(new[] { true, false, true }), top,
                "TC-005 a closed window is never an arbitration target");

            var reordered = new WindowStackCore();
            int first = reordered.Register();
            int second = reordered.Register();
            int third = reordered.Register();
            reordered.Raise(first);
            IsTrue(reordered.ZOrder.SequenceEqual(new[] { second, third, first }),
                "TC-006 raise is move-to-top and preserves the order of the rest");

            var alreadyTop = new WindowStackCore();
            int low = alreadyTop.Register();
            int high = alreadyTop.Register();
            alreadyTop.Raise(high);
            IsTrue(alreadyTop.ZOrder.SequenceEqual(new[] { low, high }),
                "TC-007 raising the top window is a no-op");

            var empty = new WindowStackCore();
            Eq(empty.TopMatching(Array.Empty<bool>()), -1, "TC-008 empty stack -> -1");
            empty.Raise(999);
            Eq(empty.ZOrder.Count, 0, "TC-008 raising an unknown handle is a silent no-op");

            var cleared = new WindowStackCore();
            cleared.Register();
            cleared.Register();
            cleared.Clear();
            Eq(cleared.ZOrder.Count, 0, "TC-009 Clear empties the stack");
            Eq(cleared.TopMatching(new[] { true, true }), -1, "TC-009 Clear -> nothing to arbitrate");

            // A child dialog (Disk Recovery) must not be buried by the panel that opened it
            // (the Drive Bay), even though clicking that panel raises it.
            var parented = new WindowStackCore();
            int other = parented.Register();
            int driveBay = parented.Register();
            int recovery = parented.Register(keepAbove: driveBay);
            parented.Raise(driveBay);
            IsTrue(parented.ZOrder.SequenceEqual(new[] { other, driveBay, recovery }),
                "TC-010 raising a parent lifts its child back above it");
            Eq(parented.TopMatching(new[] { false, true, true }), recovery,
                "TC-010 the child still wins the click where they overlap");
        }

        // A held item is deposited only over the item grid. It used to deposit anywhere on the
        // window, so clicking the search bar or a tab silently swallowed whatever you were holding.
        private static void DepositGateTests()
        {
            Section("DepositGate - deposit only over the item grid");

            // Baseline: a fresh press over an empty grid cell, Storage tab, item on the cursor.
            const bool PressEdge = true, Armed = true, StorageTab = true, HasItem = true;
            const bool NoAnimation = false, InsideGrid = true, OverOccupiedSlot = false;

            IsTrue(
                DepositGate.ShouldDeposit(PressEdge, Armed, StorageTab, HasItem, NoAnimation, InsideGrid, OverOccupiedSlot),
                "TC-101 press on an empty grid cell -> deposit");

            IsFalse(
                DepositGate.ShouldDeposit(PressEdge, Armed, StorageTab, HasItem, NoAnimation, false, OverOccupiedSlot),
                "TC-102 press outside the grid (search bar, tabs, title bar, scrollbar) -> no deposit");

            IsFalse(
                DepositGate.ShouldDeposit(PressEdge, Armed, StorageTab, HasItem, NoAnimation, InsideGrid, true),
                "TC-103 press on an occupied slot -> no deposit, OnItemClicked handles it");

            IsFalse(
                DepositGate.ShouldDeposit(false, Armed, StorageTab, HasItem, NoAnimation, InsideGrid, OverOccupiedSlot),
                "TC-104 button held rather than newly pressed -> no deposit mid-drag");

            IsFalse(
                DepositGate.ShouldDeposit(PressEdge, Armed, StorageTab, false, NoAnimation, InsideGrid, OverOccupiedSlot),
                "TC-105 empty cursor -> no deposit");

            IsFalse(
                DepositGate.ShouldDeposit(PressEdge, Armed, false, HasItem, NoAnimation, InsideGrid, OverOccupiedSlot),
                "TC-106 not the Storage tab -> no deposit");

            IsFalse(
                DepositGate.ShouldDeposit(PressEdge, Armed, StorageTab, HasItem, true, InsideGrid, OverOccupiedSlot),
                "TC-107 item use animation active -> no deposit");

            IsFalse(
                DepositGate.ShouldDeposit(PressEdge, false, StorageTab, HasItem, NoAnimation, InsideGrid, OverOccupiedSlot),
                "TC-108 the very click that opened the Terminal -> no deposit");

            // Empty storage is TC-101's input exactly (no slot is occupied, so none can be hovered);
            // that it still deposits is a wiring property of TerminalUIState, covered by IT-004.
        }

        // The frame clock used to be Main.uCount, which vanilla resets to zero every second. A
        // consume at count K therefore came back to life every time the counter wrapped onto K
        // again -- one frame per second where every click in the mod was silently dropped.
        private static void ClickBlockerTests()
        {
            Section("UIClickBlocker - consumption must not outlive its frame");

            UIClickBlocker.ResetForTests();
            UIClickBlocker.Consume();
            IsFalse(UIClickBlocker.IsConsumed,
                "TC-200 a consume before the first frame cannot latch (it would kill every click forever)");

            UIClickBlocker.ResetForTests();
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            IsFalse(UIClickBlocker.IsConsumed, "TC-201 a fresh frame starts unconsumed");

            UIClickBlocker.Consume();
            IsTrue(UIClickBlocker.IsConsumed, "TC-202 Consume() holds for the rest of the frame");

            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            IsFalse(UIClickBlocker.IsConsumed, "TC-203 the next frame starts unconsumed again");

            // The regression: consume once, then run well past a second's worth of frames and
            // assert the stale consume never resurfaces on any of them.
            UIClickBlocker.ResetForTests();
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            UIClickBlocker.Consume();

            bool resurfaced = false;
            for (int frame = 0; frame < 300; frame++)
            {
                UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
                if (UIClickBlocker.IsConsumed)
                    resurfaced = true;
            }
            IsFalse(resurfaced, "TC-204 a consume never resurfaces on a later frame (uCount wrap bug)");

            // Suppressing a window zeroes Main.mouseLeft around its update. A window that latches
            // its previous-button state from Main.mouseLeft therefore records "released" for a
            // button still held, and fires a phantom press on the first unsuppressed frame. The
            // real button state is captured at BeginFrame, where suppression cannot reach it.
            UIClickBlocker.ResetForTests();
            UIClickBlocker.BeginFrame(mouseLeft: true, mouseRight: true, mouseMiddle: true);
            bool prevLeft = UIClickBlocker.RealMouseLeft;
            bool prevRight = UIClickBlocker.RealMouseRight;
            bool prevMiddle = UIClickBlocker.RealMouseMiddle;

            IsTrue(prevLeft && prevRight && prevMiddle,
                "TC-205 every held button survives a suppressed frame");

            // Every button, not just left: a phantom right-press in the Terminal withdraws a stack
            // from storage, and a phantom middle-press starts a drag.
            UIClickBlocker.BeginFrame(mouseLeft: true, mouseRight: true, mouseMiddle: true);
            IsFalse(UIClickBlocker.RealMouseLeft && !prevLeft,
                "TC-206 a held LEFT button never looks like a fresh press");
            IsFalse(UIClickBlocker.RealMouseRight && !prevRight,
                "TC-207 a held RIGHT button never looks like a fresh press (phantom withdraw)");
            IsFalse(UIClickBlocker.RealMouseMiddle && !prevMiddle,
                "TC-208 a held MIDDLE button never looks like a fresh press");

            // A window must claim the frame's click whatever the button. Claiming only left-clicks
            // is what let a right-click be acted on by every window under the cursor at once.
            UIClickBlocker.ResetForTests();
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: true, mouseMiddle: false);
            UIClickBlocker.ClaimIfPressed(hovered: true, left: false, right: true, middle: false);
            IsTrue(UIClickBlocker.IsConsumed, "TC-209 a RIGHT-click is claimed, not just a left one");

            UIClickBlocker.ResetForTests();
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: true);
            UIClickBlocker.ClaimIfPressed(hovered: true, left: false, right: false, middle: true);
            IsTrue(UIClickBlocker.IsConsumed, "TC-210 a MIDDLE-click is claimed");

            UIClickBlocker.ResetForTests();
            UIClickBlocker.BeginFrame(mouseLeft: true, mouseRight: false, mouseMiddle: false);
            UIClickBlocker.ClaimIfPressed(hovered: false, left: true, right: false, middle: false);
            IsFalse(UIClickBlocker.IsConsumed, "TC-211 a window the cursor is not over claims nothing");

            UIClickBlocker.ResetForTests();
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            UIClickBlocker.ClaimIfPressed(hovered: true, left: false, right: false, middle: false);
            IsFalse(UIClickBlocker.IsConsumed, "TC-212 hovering with no button pressed claims nothing");

            // The z-order must not be reshuffled under the player's hand mid-drag.
            UIClickBlocker.ResetForTests();
            UIClickBlocker.BeginFrame(mouseLeft: true, mouseRight: false, mouseMiddle: false);
            IsFalse(UIClickBlocker.GestureActive, "TC-213 no gesture -> the press may raise a window");

            UIClickBlocker.MarkGesture();
            UIClickBlocker.BeginFrame(mouseLeft: true, mouseRight: false, mouseMiddle: false);
            IsTrue(UIClickBlocker.GestureActive, "TC-214 a gesture in progress blocks the raise");

            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            UIClickBlocker.BeginFrame(mouseLeft: false, mouseRight: false, mouseMiddle: false);
            IsFalse(UIClickBlocker.GestureActive, "TC-215 the gesture grace expires once it ends");
        }

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

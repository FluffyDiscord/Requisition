using System.Text;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerraStorage.Helpers.Resolver;

namespace TerraStorage.Systems
{
    // Load-time sanity check that the Terraria->core recipe adapter (TerrariaRecipeEnvironment.ToCore)
    // faithfully maps the REAL, fully-loaded recipe set. The offline unit tests exercise the resolver
    // algorithm with synthetic fixtures; this closes the one seam they cannot — that real Terraria
    // recipes convert to the equivalent CoreRecipe the algorithm was tested against. It is a pure
    // structural comparison (it passes iff ToCore is correct, so it cannot raise a false alarm),
    // read-only, and fully guarded so it can never interfere with mod loading. Result is logged once.
    public class ResolverSelfCheck : ModSystem
    {
        public override void PostAddRecipes()
        {
            try { Run(); }
            catch (System.Exception e) { Mod.Logger.Warn($"[Resolver self-check] skipped (threw): {e.Message}"); }
        }

        private void Run()
        {
            int checkedCount = 0, failures = 0;
            var detail = new StringBuilder();

            for (int i = 0; i < Recipe.numRecipes; i++)
            {
                var recipe = Main.recipe[i];
                if (recipe?.createItem == null || recipe.createItem.type <= ItemID.None) continue;
                checkedCount++;

                var core = TerrariaRecipeEnvironment.ToCore(recipe);

                int expectedIngredients = 0;
                foreach (var ing in recipe.requiredItem) if (ing.type > ItemID.None) expectedIngredients++;
                int expectedTiles = 0;
                foreach (int t in recipe.requiredTile) if (t >= 0) expectedTiles++;

                string problem = null;
                if (core.OutputType != recipe.createItem.type) problem = "output type";
                else if (core.OutputStack != recipe.createItem.stack) problem = "output stack";
                else if (core.Ingredients.Count != expectedIngredients) problem = "ingredient count";
                else if (core.RequiredTiles.Count != expectedTiles) problem = "tile count";
                else if (core.AcceptedGroups.Count != recipe.acceptedGroups.Count) problem = "group count";
                else if (!ReferenceEquals(core.Source, recipe)) problem = "source back-ref";
                else
                {
                    int idx = 0;
                    foreach (var ing in recipe.requiredItem)
                    {
                        if (ing.type <= ItemID.None) continue;
                        if (core.Ingredients[idx].Type != ing.type || core.Ingredients[idx].Stack != ing.stack)
                        { problem = $"ingredient[{idx}] {core.Ingredients[idx].Type}x{core.Ingredients[idx].Stack} != {ing.type}x{ing.stack}"; break; }
                        idx++;
                    }
                }

                if (problem != null)
                {
                    failures++;
                    if (failures <= 8)
                        detail.AppendLine($"  recipe #{i} (item {recipe.createItem.type}): {problem}");
                }
            }

            if (failures == 0)
                Mod.Logger.Info($"[Resolver self-check] PASS - adapter mapped all {checkedCount} real recipes to the resolver core faithfully.");
            else
                Mod.Logger.Warn($"[Resolver self-check] FAIL - {failures}/{checkedCount} recipes mis-mapped:\n{detail}");
        }
    }
}

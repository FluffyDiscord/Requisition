using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerraStorage.Content.Items;
using TerraStorage.Helpers;

namespace TerraStorage.Content.Tiles
{
    /// <summary>
    /// Registers crafting recipes for all placeable TerraStorage tiles (Drive Bay, Terminal,
    /// Crafting Core, and condition source tiles).
    /// </summary>
    public class TileRecipeSystem : ModSystem
    {
        public override void PostSetupContent()
        {
            RecipeResolver.RegisterTileDisplay(TileID.DemonAltar,   ModContent.ItemType<DemonAltarItem>());
            RecipeResolver.RegisterTileDisplay(TileID.DemonAltar, ModContent.ItemType<CrimsonAltarItem>());
            RecipeResolver.WarmTileCaches();
        }

        public override void AddRecipes()
        {
            // Storage Block
            Recipe.Create(ModContent.ItemType<DriveBayItem>())
                .AddIngredient(ItemID.Chest, 1)
                .AddIngredient(ItemID.CopperBar, 5)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.Anvils)
                .Register();

            Recipe.Create(ModContent.ItemType<DriveBayItem>())
                .AddIngredient(ItemID.Chest, 1)
                .AddIngredient(ItemID.TinBar, 5)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.Anvils)
                .Register();

            // Terminal
            Recipe.Create(ModContent.ItemType<TerminalItem>())
                .AddIngredient(ItemID.IronBar, 6)
                .AddIngredient(ItemID.Glass, 10)
                .AddIngredient(ItemID.MagicMirror, 1)
                .AddIngredient(ItemID.CopperBar, 3)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.WorkBenches)
                .Register();

            Recipe.Create(ModContent.ItemType<TerminalItem>())
                .AddIngredient(ItemID.LeadBar, 6)
                .AddIngredient(ItemID.Glass, 10)
                .AddIngredient(ItemID.MagicMirror, 1)
                .AddIngredient(ItemID.TinBar, 3)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.WorkBenches)
                .Register();

            Recipe.Create(ModContent.ItemType<TerminalItem>())
                .AddIngredient(ItemID.IronBar, 6)
                .AddIngredient(ItemID.Glass, 10)
                .AddIngredient(ItemID.IceMirror, 1)
                .AddIngredient(ItemID.CopperBar, 3)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.WorkBenches)
                .Register();

            Recipe.Create(ModContent.ItemType<TerminalItem>())
                .AddIngredient(ItemID.LeadBar, 6)
                .AddIngredient(ItemID.Glass, 10)
                .AddIngredient(ItemID.IceMirror, 1)
                .AddIngredient(ItemID.TinBar, 3)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.WorkBenches)
                .Register();

            // Crafting Core
            Recipe.Create(ModContent.ItemType<CraftingCoreItem>())
                .AddIngredient(ItemID.GoldBar, 12)
                .AddIngredient(ItemID.Glass, 8)
                .AddIngredient(ItemID.IronHammer, 1)
                .AddIngredient(ItemID.Ruby, 2)
                .AddTile(TileID.WorkBenches)
                .Register();

            Recipe.Create(ModContent.ItemType<CraftingCoreItem>())
                .AddIngredient(ItemID.PlatinumBar, 12)
                .AddIngredient(ItemID.Glass, 8)
                .AddIngredient(ItemID.LeadHammer, 1)
                .AddIngredient(ItemID.Ruby, 2)
                .AddTile(TileID.WorkBenches)
                .Register();

            // Bottomless condition providers (crafted from 99 of the regular bucket at a Hardmode anvil)
            Recipe.Create(ItemID.BottomlessBucket)
                .AddIngredient(ItemID.WaterBucket, 99)
                .AddTile(TileID.MythrilAnvil)
                .Register();

            Recipe.Create(ItemID.BottomlessLavaBucket)
                .AddIngredient(ItemID.LavaBucket, 99)
                .AddTile(TileID.MythrilAnvil)
                .Register();

            Recipe.Create(ItemID.BottomlessHoneyBucket)
                .AddIngredient(ItemID.HoneyBucket, 99)
                .AddTile(TileID.MythrilAnvil)
                .Register();

            // Remote Terminal — post-Moon Lord recipe (default)
            var condHard = new Condition("Mods.TerraStorage.Conditions.RemoteTerminalHard",
                () => !(TerraStorageConfig.Instance?.EasierRemoteTerminal ?? false));
            Recipe.Create(ModContent.ItemType<RemoteTerminal>())
                .AddIngredient(ItemID.LunarBar, 8)
                .AddIngredient(ItemID.FragmentStardust, 10)
                .AddIngredient(ItemID.FragmentNebula, 10)
                .AddIngredient(ItemID.Wire, 20)
                .AddIngredient(ItemID.DontStarveShaderItem, 1)
                .AddTile(TileID.LunarCraftingStation)
                .AddCondition(condHard)
                .Register();

            // Remote Terminal — easier recipe (post-Skeletron + Deerclops, config option)
            var condEasy = new Condition("Mods.TerraStorage.Conditions.RemoteTerminalEasy",
                () => TerraStorageConfig.Instance?.EasierRemoteTerminal ?? false);
            Recipe.Create(ModContent.ItemType<RemoteTerminal>())
                .AddIngredient(ItemID.HellstoneBar, 15)
                .AddIngredient(ItemID.Bone, 10)
                .AddIngredient(ItemID.Wire, 20)
                .AddIngredient(ItemID.DontStarveShaderItem, 1)
                .AddTile(TileID.Hellforge)
                .AddCondition(condEasy)
                .Register();

            // Demon Altar (craftable at either altar type)
            Recipe.Create(ModContent.ItemType<DemonAltarItem>())
                .AddIngredient(ItemID.DemoniteBar, 10)
                .AddIngredient(ItemID.ShadowScale, 5)
                .AddTile(TileID.DemonAltar)
                .Register();

            Recipe.Create(ModContent.ItemType<DemonAltarItem>())
                .AddIngredient(ItemID.DemoniteBar, 10)
                .AddIngredient(ItemID.ShadowScale, 5)
                .AddTile(TileID.DemonAltar)
                .Register();

            // Crimson Altar (craftable at either altar type)
            Recipe.Create(ModContent.ItemType<CrimsonAltarItem>())
                .AddIngredient(ItemID.CrimtaneBar, 10)
                .AddIngredient(ItemID.TissueSample, 5)
                .AddTile(TileID.DemonAltar)
                .Register();

            Recipe.Create(ModContent.ItemType<CrimsonAltarItem>())
                .AddIngredient(ItemID.CrimtaneBar, 10)
                .AddIngredient(ItemID.TissueSample, 5)
                .AddTile(TileID.DemonAltar)
                .Register();
        }
    }
}

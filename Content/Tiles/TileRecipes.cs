using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerraStorage.Content.Items;
using TerraStorage.Helpers;

namespace TerraStorage.Content.Tiles
{
    // Registers crafting recipes for all placeable Requisition tiles (Drive Bay, Terminal,
    // Crafting Core, and condition source tiles). 
    public class TileRecipeSystem : ModSystem
    {
        internal const string GroupCopperBar  = "TerraStorage:AnyCopperBar";
        internal const string GroupGoldBar    = "TerraStorage:AnyGoldBar";
        internal const string GroupIronHammer = "TerraStorage:AnyIronHammer";

        public override void AddRecipeGroups()
        {
            RecipeGroup.RegisterGroup(GroupCopperBar,
                new RecipeGroup(() => "Any Copper Bar", ItemID.CopperBar, ItemID.TinBar));
            RecipeGroup.RegisterGroup(GroupGoldBar,
                new RecipeGroup(() => "Any Gold Bar", ItemID.GoldBar, ItemID.PlatinumBar));
            RecipeGroup.RegisterGroup(GroupIronHammer,
                new RecipeGroup(() => "Any Iron Hammer", ItemID.IronHammer, ItemID.LeadHammer));
        }

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
                .AddRecipeGroup(GroupCopperBar, 5)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.Anvils)
                .Register();

            // Terminal
            Recipe.Create(ModContent.ItemType<TerminalItem>())
                .AddRecipeGroup(RecipeGroupID.IronBar, 6)
                .AddIngredient(ItemID.Glass, 10)
                .AddIngredient(ItemID.MagicMirror, 1)
                .AddRecipeGroup(GroupCopperBar, 3)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.WorkBenches)
                .Register();

            Recipe.Create(ModContent.ItemType<TerminalItem>())
                .AddRecipeGroup(RecipeGroupID.IronBar, 6)
                .AddIngredient(ItemID.Glass, 10)
                .AddIngredient(ItemID.IceMirror, 1)
                .AddRecipeGroup(GroupCopperBar, 3)
                .AddIngredient(ItemID.Diamond, 1)
                .AddTile(TileID.WorkBenches)
                .Register();

            // Crafting Core
            Recipe.Create(ModContent.ItemType<CraftingCoreItem>())
                .AddRecipeGroup(GroupGoldBar, 12)
                .AddIngredient(ItemID.Glass, 8)
                .AddRecipeGroup(GroupIronHammer, 1)
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
                () => !(RequisitionConfig.Instance?.EasierRemoteTerminal ?? false));
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
                () => RequisitionConfig.Instance?.EasierRemoteTerminal ?? false);
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

            // Crimson Altar (craftable at either altar type)
            Recipe.Create(ModContent.ItemType<CrimsonAltarItem>())
                .AddIngredient(ItemID.CrimtaneBar, 10)
                .AddIngredient(ItemID.TissueSample, 5)
                .AddTile(TileID.DemonAltar)
                .Register();

        }
    }
}

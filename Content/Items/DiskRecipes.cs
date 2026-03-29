using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerraStorage.Content.Tiles;

namespace TerraStorage.Content.Items
{
    // Registers Storage Disk crafting recipes. Only the initial Tier 1 recipe is registered
    // here. Tier 2–6 disks are obtained exclusively by upgrading through the Terminal's
    // Disks tab, which preserves the disk's GUID and stored items.
    public class DiskRecipeSystem : ModSystem
    {
        public override void AddRecipes()
        {
            // Tier 1: Basic Storage Disk (64 stacks)
            Recipe.Create(ModContent.ItemType<StorageDiskTier1>())
                .AddRecipeGroup(TileRecipeSystem.GroupGoldBar, 10)
                .AddIngredient(ItemID.Glass, 3)
                .AddIngredient(ItemID.Lens, 1)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}

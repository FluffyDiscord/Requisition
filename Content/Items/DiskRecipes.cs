using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerraStorage.Content.Items
{
    /// <summary>
    /// Registers Storage Disk crafting recipes. Only the initial Tier 1 recipe is registered
    /// here. Tier 2–6 disks are obtained exclusively by upgrading through the Terminal's
    /// Disks tab, which preserves the disk's GUID and stored items.
    /// </summary>
    public class DiskRecipeSystem : ModSystem
    {
        public override void AddRecipes()
        {
            // Tier 1: Basic Storage Disk (64 stacks)
            Recipe.Create(ModContent.ItemType<StorageDiskTier1>())
                .AddIngredient(ItemID.GoldBar, 10)
                .AddIngredient(ItemID.Glass, 3)
                .AddIngredient(ItemID.Lens, 1)
                .AddTile(TileID.Anvils)
                .Register();

            Recipe.Create(ModContent.ItemType<StorageDiskTier1>())
                .AddIngredient(ItemID.PlatinumBar, 10)
                .AddIngredient(ItemID.Glass, 3)
                .AddIngredient(ItemID.Lens, 1)
                .AddTile(TileID.Anvils)
                .Register();
        }
    }
}

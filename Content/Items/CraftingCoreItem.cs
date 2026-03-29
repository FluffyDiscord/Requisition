using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerraStorage.Content.Tiles;

namespace TerraStorage.Content.Items
{
    // Placeable item that creates a CraftingCore tile.
    // The Crafting Core holds virtual crafting station items and condition providers,
    // making them available to any connected Terminal for auto-crafting.
    public class CraftingCoreItem : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 32;
            Item.maxStack = 99;
            Item.useTurn = true;
            Item.autoReuse = true;
            Item.useAnimation = 15;
            Item.useTime = 10;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.consumable = true;
            Item.value = Item.buyPrice(gold: 8);
            Item.rare = ItemRarityID.Orange;
            Item.createTile = ModContent.TileType<CraftingCore>();
        }

    }
}

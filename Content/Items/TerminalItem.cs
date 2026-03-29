using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using TerraStorage.Content.Tiles;

namespace TerraStorage.Content.Items
{
    // Placeable item that creates a Terminal tile.
    // The Terminal is the primary interface for browsing and withdrawing from connected storage.
    public class TerminalItem : ModItem
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
            Item.value = Item.buyPrice(gold: 10);
            Item.rare = ItemRarityID.Green;
            Item.createTile = ModContent.TileType<Terminal>();
        }

    }
}

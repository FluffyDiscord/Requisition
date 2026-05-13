using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Requisition.Content.Tiles;

namespace Requisition.Content.Items
{
    // Placeable item that creates a <see cref="Requisition.Content.Tiles.DriveBay"/> tile (Drive Bay).
    // Stacks up to 99 so players can carry multiple units before placing them.
    public class DriveBayItem : ModItem
    {
        public override string Texture => "Requisition/Content/Tiles/DriveBay";

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
            Item.value = Item.buyPrice(gold: 5);
            Item.rare = ItemRarityID.Blue;
            Item.createTile = ModContent.TileType<DriveBayLarge>();
        }

    }
}

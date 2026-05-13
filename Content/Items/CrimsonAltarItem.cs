using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace Requisition.Content.Items
{
    // Station item for the Crafting Core. Provides TileID.DemonAltar so recipes
    // that require a Crimson Altar can be crafted via the Terminal.
    // (Crimson and Demon altars share TileID.DemonAltar = 26 in vanilla.)
    public class CrimsonAltarItem : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 32;
            Item.maxStack = 99;
            Item.value = Item.buyPrice(gold: 5);
            Item.rare = ItemRarityID.Orange;
            Item.createTile = TileID.DemonAltar;
        }
    }
}

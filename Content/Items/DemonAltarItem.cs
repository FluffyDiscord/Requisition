using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerraStorage.Content.Items
{
    /// <summary>
    /// Station item for the Crafting Core. Provides TileID.DemonAltar so recipes
    /// that require a Demon Altar can be crafted via the Terminal.
    /// </summary>
    public class DemonAltarItem : ModItem
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

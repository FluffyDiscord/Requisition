using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using TerraStorage.Content.Items;
using TerraStorage.Content.Tiles;
using TerraStorage.Content.UI;

namespace TerraStorage.Content.Players
{
    // Handles middle-click activation of the Remote Terminal from the player inventory.
    public class RemoteTerminalPlayer : ModPlayer
    {
        private bool _prevMiddle;

        public override void PostUpdate()
        {
            if (Player != Main.LocalPlayer) return;
            if (!Main.playerInventory) return;

            bool middle = Main.mouseMiddle;
            bool clicked = middle && !_prevMiddle;
            _prevMiddle = middle;

            if (!clicked) return;
            if (Main.HoverItem == null || Main.HoverItem.IsAir) return;
            if (Main.HoverItem.ModItem is not RemoteTerminal hoverRemote) return;

            int boundId = hoverRemote.BoundEntityId;

            if (boundId < 0)
            {
                Main.NewText("Remote Terminal is not bound. Right-click a Crafting Terminal to bind.", Color.Yellow);
                return;
            }

            if (!TileEntity.ByID.TryGetValue(boundId, out var te) || te is not TerminalEntity terminal)
            {
                Main.NewText("Bound Terminal not found. The Terminal may have been destroyed.", Color.OrangeRed);
                return;
            }

            ModContent.GetInstance<TerminalUISystem>()?.OpenTerminalRemote(terminal);
        }
    }
}

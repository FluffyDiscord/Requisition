using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Requisition.Content.Items;
using Requisition.Content.Tiles;
using Requisition.Content.UI;

namespace Requisition.Content.Players
{
    // Handles middle-click and hotkey activation of the Remote Terminal.
    public class RemoteTerminalPlayer : ModPlayer
    {
        private bool _prevMiddle;

        public override void PostUpdate()
        {
            if (Player != Main.LocalPlayer) return;

            HandleHotkey();

            if (!Main.playerInventory) return;

            bool middle = Main.mouseMiddle;
            bool clicked = middle && !_prevMiddle;
            _prevMiddle = middle;

            if (!clicked) return;
            if (Main.HoverItem == null || Main.HoverItem.IsAir) return;
            if (Main.HoverItem.ModItem is not RemoteTerminal hoverRemote) return;

            OpenRemote(hoverRemote);
        }

        private void HandleHotkey()
        {
            if (!(Requisition.OpenRemoteTerminalHotkey?.JustPressed ?? false)) return;

            for (int i = 0; i < 50; i++)
            {
                if (Player.inventory[i].ModItem is RemoteTerminal remote)
                {
                    OpenRemote(remote);
                    return;
                }
            }
        }

        private void OpenRemote(RemoteTerminal remote)
        {
            int boundId = remote.BoundEntityId;

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

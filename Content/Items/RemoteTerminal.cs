using System.IO;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using TerraStorage.Content.Tiles;
using TerraStorage.Content.UI;

namespace TerraStorage.Content.Items
{
    // Binds to a placed Terminal and opens its full UI from anywhere in the world via left-click use,
    // middle-click, or hotkey. Right-click a Terminal tile while holding this to bind it.
    public class RemoteTerminal : ModItem
    {
        private int _boundEntityId = -1;
        public int BoundEntityId { get => _boundEntityId; set => _boundEntityId = value; }

        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 32;
            Item.maxStack = 1;
            Item.rare = ItemRarityID.Red;
            Item.value = Item.buyPrice(gold: 20);
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.useTime = 20;
            Item.useAnimation = 20;
        }

        public override bool? UseItem(Player player)
        {
            if (player.whoAmI != Main.myPlayer) return true;

            if (_boundEntityId < 0)
            {
                Main.NewText("Remote Terminal is not bound. Right-click a Crafting Terminal to bind.", Color.Yellow);
                return true;
            }

            if (!TileEntity.ByID.TryGetValue(_boundEntityId, out var te) || te is not TerminalEntity terminal)
            {
                Main.NewText("Bound Terminal not found. The Terminal may have been destroyed.", Color.OrangeRed);
                return true;
            }

            ModContent.GetInstance<TerminalUISystem>()?.OpenTerminalRemote(terminal);
            return true;
        }

        public override void SaveData(TagCompound tag)
        {
            if (_boundEntityId >= 0)
                tag["boundId"] = _boundEntityId;
        }

        public override void LoadData(TagCompound tag)
        {
            _boundEntityId = tag.ContainsKey("boundId") ? tag.GetInt("boundId") : -1;
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(_boundEntityId);
        }

        public override void NetReceive(BinaryReader reader)
        {
            _boundEntityId = reader.ReadInt32();
        }
    }
}

using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace Requisition.Content.Items
{
    // Binds to a placed Terminal and opens its full UI from anywhere in the world via middle-click.
    // Right-click a Terminal tile while holding this to bind it.
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
            Item.useStyle = ItemUseStyleID.None;
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

using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Requisition.Content.Tiles;
using Requisition.Helpers;

namespace Requisition.Systems
{
    internal static class QuickStackSystem
    {
        private const float RangeTiles = 15f;
        private const float RangePixels = RangeTiles * 16f;
        private const float RangeSq = RangePixels * RangePixels;

        internal static void OnQuickStackAllChests(On_Player.orig_QuickStackAllChests orig, Player self)
        {
            orig(self);
            if (self.whoAmI != Main.myPlayer) return;
            QuickStackToNearbyTerminals(self);
        }

        private static void QuickStackToNearbyTerminals(Player player)
        {
            var terminals = FindNearbyTerminals(player);
            if (terminals.Count == 0) return;

            if (Main.netMode == Terraria.ID.NetmodeID.SinglePlayer)
            {
                foreach (var terminal in terminals)
                {
                    var diskIds = StorageNetwork.GetAllConnectedDiskIds(terminal.Position);
                    if (diskIds.Count == 0) continue;

                    var existingTypes = StorageWorldSystem.Instance.GetItemCounts(diskIds);

                    for (int i = 10; i < 50; i++)
                    {
                        var item = player.inventory[i];
                        if (item.IsAir || item.favorited || item.IsACoin) continue;
                        if (!existingTypes.ContainsKey(item.type)) continue;

                        int leftover = StorageWorldSystem.Instance.InsertItem(diskIds, item);
                        if (leftover <= 0)
                            item.TurnToAir();
                        else
                            item.stack = leftover;
                    }
                }
            }
            else if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
            {
                var mod = Terraria.ModLoader.ModContent.GetInstance<Requisition>();
                foreach (var terminal in terminals)
                    NetworkHandler.SendQuickStackToStorage(mod, terminal.Position, player);
            }
        }

        private static List<TerminalEntity> FindNearbyTerminals(Player player)
        {
            var results = new List<TerminalEntity>();
            foreach (var kvp in TileEntity.ByID)
            {
                if (kvp.Value is TerminalEntity terminal)
                {
                    float dx = player.Center.X - (terminal.Position.X * 16f + 24f);
                    float dy = player.Center.Y - (terminal.Position.Y * 16f + 24f);
                    if (dx * dx + dy * dy <= RangeSq)
                        results.Add(terminal);
                }
            }
            return results;
        }
    }
}

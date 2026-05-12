using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ObjectData;
using TerraStorage.Content.UI;
using TerraStorage.Common;
using TerraStorage.Helpers;

namespace TerraStorage.Content.Tiles
{
    // Tile entity attached to each placed Terminal. Provides the bridge between the UI and
    // <see cref="Helpers.StorageNetwork"/>, which discovers all connected Drive Bays
    // and Crafting Cores within the search radius.
    public class TerminalEntity : ModTileEntity
    {
        public override bool IsTileValidForEntity(int x, int y)
        {
            var tile = Main.tile[x, y];
            return tile.HasTile && tile.TileType == ModContent.TileType<Terminal>();
        }

        public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
        {
            // With processedCoordinates: true, i/j is the top-left corner of the multi-tile.
            if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
            {
                // Clients cannot place tile entities directly; request server creation
                NetMessage.SendTileSquare(Main.myPlayer, i, j, 3, 3);
                NetMessage.SendData(Terraria.ID.MessageID.TileEntityPlacement, number: i, number2: j, number3: Type);
                return -1;
            }

            return Place(i, j);
        }

        // Get all disk IDs from all storage blocks connected to this terminal.
        public List<Guid> GetConnectedDiskIds()
        {
            return StorageNetwork.GetAllConnectedDiskIds(Position);
        }

        // Get both stations and conditions in a single tile-entity scan.
        public (HashSet<int> stations, HashSet<CraftingCondition> conditions) GetStationsAndConditions()
        {
            return StorageNetwork.GetAllStationsAndConditions(Position);
        }

        // Open the terminal UI for the player.
        public void OpenTerminalUI(Player player)
        {
            foreach (var bay in StorageNetwork.FindConnectedDriveBays(Position))
                bay.RefreshVisualState(true);
            var uiSystem = ModContent.GetInstance<TerminalUISystem>();
            uiSystem?.OpenTerminal(this);
        }

        // Find the TerminalEntity at a given tile position (accounts for multi-tile).
        public static TerminalEntity FindEntity(int i, int j)
        {
            var tile = Main.tile[i, j];
            if (!tile.HasTile)
                return null;

            // Entity is stored at the top-left corner of the multi-tile.
            Point16 topLeft = TileObjectData.TopLeft(i, j);
            if (topLeft == Point16.NegativeOne)
                return null;

            if (TileEntity.ByPosition.TryGetValue(topLeft, out var entity) && entity is TerminalEntity te)
                return te;

            return null;
        }
    }
}

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
    /// <summary>
    /// Tile entity attached to each placed Terminal. Provides the bridge between the UI and
    /// <see cref="Helpers.StorageNetwork"/>, which discovers all connected Drive Bays
    /// and Crafting Cores within the search radius.
    /// </summary>
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

        /// <summary>
        /// Get all disk IDs from all storage blocks connected to this terminal.
        /// </summary>
        public List<Guid> GetConnectedDiskIds()
        {
            return StorageNetwork.GetAllConnectedDiskIds(Position);
        }

        /// <summary>
        /// Get all crafting station tile types available from connected Crafting Cores.
        /// </summary>
        public HashSet<int> GetAvailableStations()
        {
            return StorageNetwork.GetAllAvailableStations(Position);
        }

        /// <summary>
        /// Get all crafting conditions available from connected Crafting Cores.
        /// </summary>
        public HashSet<CraftingCondition> GetAvailableConditions()
        {
            return StorageNetwork.GetAllAvailableConditions(Position);
        }

        /// <summary>
        /// Get both stations and conditions in a single tile-entity scan.
        /// </summary>
        public (HashSet<int> stations, HashSet<CraftingCondition> conditions) GetStationsAndConditions()
        {
            return StorageNetwork.GetAllStationsAndConditions(Position);
        }

        /// <summary>
        /// Open the terminal UI for the player.
        /// </summary>
        public void OpenTerminalUI(Player player)
        {
            var uiSystem = ModContent.GetInstance<TerminalUISystem>();
            uiSystem?.OpenTerminal(this);
        }

        /// <summary>
        /// Find the TerminalEntity at a given tile position (accounts for multi-tile).
        /// </summary>
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

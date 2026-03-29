using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using TerraStorage.Content.Tiles;
using TerraStorage.Common;

namespace TerraStorage.Helpers
{
    // Utility class that discovers all Drive Bay and Crafting Core tile entities
    // within a fixed tile radius of a Terminal, forming the "network" for a given Terminal.
    // 
    public static class StorageNetwork
    {
        // Maximum tile-distance (Euclidean) from a Terminal at which a Drive Bay
        // or Crafting Core is considered part of its network.
        public const int SearchRadius = 30;

        // Precomputed to avoid per-entity multiplication
        private const int SearchRadiusSq = SearchRadius * SearchRadius;

        // Find all DriveBayEntity instances within radius of the given tile position. 
        public static List<DriveBayEntity> FindConnectedDriveBays(Point16 terminalPosition)
        {
            var results = new List<DriveBayEntity>();
            int tx = terminalPosition.X;
            int ty = terminalPosition.Y;

            foreach (var kvp in TileEntity.ByID)
            {
                if (kvp.Value is DriveBayEntity storageEntity)
                {
                    int dx = tx - storageEntity.Position.X;
                    int dy = ty - storageEntity.Position.Y;
                    if (dx * dx + dy * dy <= SearchRadiusSq)
                        results.Add(storageEntity);
                }
            }

            return results;
        }

        // Find all CraftingCoreEntity instances within radius of the given tile position. 
        public static List<CraftingCoreEntity> FindConnectedCraftingCores(Point16 terminalPosition)
        {
            var results = new List<CraftingCoreEntity>();
            int tx = terminalPosition.X;
            int ty = terminalPosition.Y;

            foreach (var kvp in TileEntity.ByID)
            {
                if (kvp.Value is CraftingCoreEntity craftingCore)
                {
                    int dx = tx - craftingCore.Position.X;
                    int dy = ty - craftingCore.Position.Y;
                    if (dx * dx + dy * dy <= SearchRadiusSq)
                        results.Add(craftingCore);
                }
            }

            return results;
        }

        // Get all disk IDs from all storage blocks connected to a terminal position.
        public static List<Guid> GetAllConnectedDiskIds(Point16 terminalPosition)
        {
            // De-duplicate across multiple Drive Bays in case the same disk ID somehow appears twice
            var seen = new HashSet<Guid>();
            var diskIds = new List<Guid>();
            var blocks = FindConnectedDriveBays(terminalPosition);

            foreach (var block in blocks)
            {
                foreach (var id in block.GetInsertedDiskIds())
                {
                    if (seen.Add(id))
                        diskIds.Add(id);
                }
            }

            return diskIds;
        }

        // Get all stations and conditions from connected Crafting Cores in a single scan
        // of the tile entity dictionary, avoiding a second full iteration.
        public static (HashSet<int> stations, HashSet<CraftingCondition> conditions) GetAllStationsAndConditions(Point16 terminalPosition)
        {
            var stations = new HashSet<int>();
            var conditions = new HashSet<CraftingCondition>();
            int tx = terminalPosition.X;
            int ty = terminalPosition.Y;

            foreach (var kvp in TileEntity.ByID)
            {
                if (kvp.Value is CraftingCoreEntity core)
                {
                    int dx = tx - core.Position.X;
                    int dy = ty - core.Position.Y;
                    if (dx * dx + dy * dy <= SearchRadiusSq)
                    {
                        stations.UnionWith(core.GetAvailableTileTypes());
                        conditions.UnionWith(core.GetAvailableConditions());
                    }
                }
            }

            return (stations, conditions);
        }

        // Get all tile types available as crafting stations from connected Crafting Cores. 
        public static HashSet<int> GetAllAvailableStations(Point16 terminalPosition)
        {
            var (stations, _) = GetAllStationsAndConditions(terminalPosition);
            return stations;
        }

        // Get all crafting conditions available from connected Crafting Cores.
        public static HashSet<CraftingCondition> GetAllAvailableConditions(Point16 terminalPosition)
        {
            var (_, conditions) = GetAllStationsAndConditions(terminalPosition);
            return conditions;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Terraria.ID;
using Terraria.ModLoader;

namespace Requisition.Helpers
{
    // Resolves Terraria's adjTile chains so that higher-tier crafting stations
    // (e.g. Mythril Anvil) are recognized as valid substitutes for lower-tier
    // ones (e.g. Anvil) when checking recipe station requirements.
    // Results are lazily computed and cached per tile type.
    public static class AdjTileHelper
    {
        private static readonly Dictionary<int, int[]> _cache = new();

        // Vanilla adjTile chain mappings (from Player.AdjTiles() source).
        // Key = tile type, Value = tile types it also counts as.
        // Note: TitaniumForge/OrichalcumAnvil share tile IDs with
        // AdamantiteForge/MythrilAnvil (different styles, same tile type).
        private static readonly Dictionary<int, int[]> VanillaChains = new()
        {
            [TileID.AdamantiteForge] = new int[] { TileID.Hellforge, TileID.Furnaces },
            [TileID.Hellforge]       = new int[] { TileID.Furnaces },
            [TileID.GlassKiln]       = new int[] { TileID.Furnaces },
            [TileID.MythrilAnvil]    = new int[] { TileID.Anvils },
            [TileID.BewitchingTable] = new int[] { TileID.Tables },
            [469]                    = new int[] { TileID.Tables },    // Tables2
            [TileID.AlchemyTable]    = new int[] { TileID.Bottles },
        };

        // Returns all tile types that <paramref name="tileType"/> also counts as
        // via the adjTile system. For example, Mythril Anvil returns [Anvil].
        // The result does NOT include <paramref name="tileType"/> itself.
        public static int[] GetAdjTiles(int tileType)
        {
            if (_cache.TryGetValue(tileType, out var cached))
                return cached;

            var result = Resolve(tileType);
            _cache[tileType] = result;
            return result;
        }

        // Expands a set of primary tile types to include all adjTile equivalents.
        // Modifies <paramref name="tileTypes"/> in place. 
        public static void ExpandAll(HashSet<int> tileTypes)
        {
            var primary = new List<int>(tileTypes);
            foreach (int t in primary)
            {
                foreach (int adj in GetAdjTiles(t))
                    tileTypes.Add(adj);
            }
        }

        private static int[] Resolve(int tileType)
        {
            var result = new HashSet<int>();
            ResolveRecursive(tileType, result);
            return result.Count > 0 ? result.ToArray() : Array.Empty<int>();
        }

        private static void ResolveRecursive(int tileType, HashSet<int> result)
        {
            foreach (int adj in GetDirectAdjTiles(tileType))
            {
                if (result.Add(adj))
                    ResolveRecursive(adj, result);
            }
        }

        private static int[] GetDirectAdjTiles(int tileType)
        {
            // Modded tiles declare their equivalences via ModTile.AdjTiles
            var modTile = TileLoader.GetTile(tileType);
            if (modTile?.AdjTiles != null && modTile.AdjTiles.Length > 0)
                return modTile.AdjTiles;

            // Vanilla hardcoded chains
            if (VanillaChains.TryGetValue(tileType, out var chain))
                return chain;

            return Array.Empty<int>();
        }

        // Clears the cache. Call on world unload to handle mod reloads
        // that may change adjTile registrations.
        public static void ClearCache() => _cache.Clear();
    }
}

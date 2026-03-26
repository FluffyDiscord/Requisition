using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ObjectData;
using TerraStorage.Content.Tiles;

namespace TerraStorage.Content.Tiles
{
    /// <summary>
    /// Prevents breaking the floor block directly underneath an occupied multi-tile
    /// (2x2) so the device above doesn't lose support and destroy its contents.
    /// </summary>
    public class SupportProtectionGlobalTile : GlobalTile
    {
        // Both DriveBay and CraftingCore are 2x2 with the entity stored at top-left.
        private const int DeviceSize = 2;

        public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged)
        {
            // The tile being mined is the "floor" immediately beneath the device's bottom row.
            // bottomRowY = topLeftY + (DeviceSize - 1), so floorY = topLeftY + DeviceSize.
            int topLeftY = j - DeviceSize;
            if (topLeftY < 0)
                return true;

            // Floor tile at x = topLeftX can correspond to either the device's left or right column.
            // Candidate top-left X positions that could have their bottom tile under this floor tile:
            // [i-1, i]
            for (int topLeftX = i - 1; topLeftX <= i; topLeftX++)
            {
                var pos = new Point16(topLeftX, topLeftY);
                if (!TileEntity.ByPosition.TryGetValue(pos, out var entity))
                    continue;

                if (entity is DriveBayEntity driveBay && driveBay.HasDisks())
                {
                    blockDamaged = false;
                    return false;
                }

                if (entity is CraftingCoreEntity craftingCore && craftingCore.HasStations())
                {
                    blockDamaged = false;
                    return false;
                }
            }

            return true;
        }
    }
}


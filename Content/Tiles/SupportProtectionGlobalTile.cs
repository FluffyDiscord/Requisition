using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace TerraStorage.Content.Tiles
{
    // Prevents breaking the floor block directly underneath an occupied multi-tile
    // so the device above doesn't lose support and destroy its contents.
    public class SupportProtectionGlobalTile : GlobalTile
    {
        // Each entry: (width, height) of a protected device type.
        private static readonly (int w, int h)[] DeviceSizes = { (3, 3), (2, 3) };

        public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged)
        {
            foreach (var (w, h) in DeviceSizes)
            {
                // The floor tile at (i, j) sits directly below the device's bottom row.
                // topLeftY = j - h (since bottom row is topLeftY + h - 1, floor is topLeftY + h).
                int topLeftY = j - h;
                if (topLeftY < 0) continue;

                // The floor tile could be under any column of the device.
                for (int topLeftX = i - (w - 1); topLeftX <= i; topLeftX++)
                {
                    if (topLeftX < 0) continue;
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
            }

            return true;
        }
    }
}

using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent.ObjectInteractions;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace TerraStorage.Content.Tiles
{
    // The Drive Bay tile (2x2). Houses up to <see cref="DriveBayEntity.DiskSlotCount"/> Storage Disks.
    // Blocks tile destruction while any disk is inserted and opens the disk-management UI on right-click.
    public class DriveBay : ModTile
    {
        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLavaDeath[Type] = false;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style2x2);
            TileObjectData.newTile.Origin = new Point16(0, 1);
            TileObjectData.newTile.CoordinateHeights = new[] { 16, 16 };
            TileObjectData.newTile.CoordinatePadding = 0;
            TileObjectData.newTile.Width = 2;
            TileObjectData.newTile.Height = 2;
            TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
                ModContent.GetInstance<DriveBayEntity>().Hook_AfterPlacement, -1, 0, true);
            TileObjectData.newTile.UsesCustomCanPlace = true;
            TileObjectData.newTile.AnchorBottom = new AnchorData(AnchorType.SolidTile | AnchorType.SolidWithTop, 2, 0);
            TileObjectData.addTile(Type);

            AddMapEntry(new Color(100, 100, 150), CreateMapEntryName());
        }

        public override bool HasSmartInteract(int i, int j, SmartInteractScanSettings settings) => true;

        public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            var entity = DriveBayEntity.FindEntity(i, j);
            if (entity != null && entity.HasDisks())
            {
                // Prevent breaking the block while disks are inserted to avoid accidental data loss
                blockDamaged = false;
                return false;
            }
            return true;
        }

        public override bool RightClick(int i, int j)
        {
            var entity = DriveBayEntity.FindEntity(i, j);
            if (entity != null)
            {
                entity.OpenDiskUI(Main.LocalPlayer);
                return true;
            }
            return false;
        }

        public override void MouseOver(int i, int j)
        {
            var player = Main.LocalPlayer;
            player.noThrow = 2;
            player.cursorItemIconEnabled = true;
            player.cursorItemIconID = ModContent.ItemType<Items.DriveBayItem>();
        }

        public override void KillMultiTile(int i, int j, int frameX, int frameY)
        {
            // Drop any remaining disks before removing the tile entity
            var entity = DriveBayEntity.FindEntity(i, j);
            if (entity != null)
            {
                entity.DropDisks(i, j);
            }

            // i, j is the top-left corner (tModLoader guarantees this).
            // The entity is stored at top-left, matching where Hook_AfterPlacement placed it.
            ModContent.GetInstance<DriveBayEntity>().Kill(i, j);
        }
    }
}

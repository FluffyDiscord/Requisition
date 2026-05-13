using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.GameContent.ObjectInteractions;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace Requisition.Content.Tiles
{
    // The Drive Bay tile (3x3). Houses up to <see cref="DriveBayEntity.DiskSlotCount"/> Storage Disks.
    // Blocks tile destruction while any disk is inserted and opens the disk-management UI on right-click.
    public class DriveBayLarge : ModTile
    {
        public override string Texture => "Requisition/Content/Tiles/DriveBay";

        private const string StatesPath = "Requisition/Content/Tiles/DriveBayStates/";

        // Bay-level status light textures (indexed by bay state: 0=Offline, 1=Online, 2=80%, 3=Full)
        private Asset<Texture2D>[] _bayLight;
        // Per-drive status light textures (same index scheme)
        private Asset<Texture2D>[] _driveLight;

        public override void Load()
        {
            _bayLight = new[]
            {
                ModContent.Request<Texture2D>(StatesPath + "DriveBayLight-Offline"),
                ModContent.Request<Texture2D>(StatesPath + "DriveBayLight-Online"),
                ModContent.Request<Texture2D>(StatesPath + "DriveBayLight-80"),
                ModContent.Request<Texture2D>(StatesPath + "DriveBayLight-100"),
            };
            _driveLight = new[]
            {
                ModContent.Request<Texture2D>(StatesPath + "DriveLight-Offline"),
                ModContent.Request<Texture2D>(StatesPath + "DriveLight-Online"),
                ModContent.Request<Texture2D>(StatesPath + "DriveLight-80"),
                ModContent.Request<Texture2D>(StatesPath + "DriveLight-100"),
            };
        }

        public override void Unload()
        {
            _bayLight = null;
            _driveLight = null;
        }

        public override void SetStaticDefaults()
        {
            Main.tileFrameImportant[Type] = true;
            Main.tileNoAttach[Type] = true;
            Main.tileLavaDeath[Type] = false;

            TileObjectData.newTile.CopyFrom(TileObjectData.Style3x3);
            TileObjectData.newTile.Origin = new Point16(1, 2);
            TileObjectData.newTile.CoordinateHeights = new[] { 16, 16, 16 };
            TileObjectData.newTile.CoordinatePadding = 0;
            TileObjectData.newTile.Width = 3;
            TileObjectData.newTile.Height = 3;
            TileObjectData.newTile.HookPostPlaceMyPlayer = new PlacementHook(
                ModContent.GetInstance<DriveBayEntity>().Hook_AfterPlacement, -1, 0, true);
            TileObjectData.newTile.UsesCustomCanPlace = true;
            TileObjectData.newTile.AnchorBottom = new AnchorData(AnchorType.SolidTile | AnchorType.SolidWithTop, 3, 0);
            TileObjectData.addTile(Type);

            AddMapEntry(new Color(100, 100, 150), CreateMapEntryName());
        }

        public override bool HasSmartInteract(int i, int j, SmartInteractScanSettings settings) => true;

        public override bool CanKillTile(int i, int j, ref bool blockDamaged)
        {
            var entity = DriveBayEntity.FindEntity(i, j);
            if (entity != null && entity.HasDisks())
            {
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
            // i, j is the top-left corner (tModLoader guarantees this).
            // The entity is stored at top-left, matching where Hook_AfterPlacement placed it.
            // CanKillTile prevents destruction while disks are present, so no disk cleanup needed here.
            ModContent.GetInstance<DriveBayEntity>().Kill(i, j);
        }

        // Drive light dimensions (2×6 px) and bay light dimensions (4×2 px).
        private const int DLW = 2, DLH = 6, BLW = 4, BLH = 2;
        private const int BLX = 38, BLY = 44;

        public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
        {
            // Skip ghost tiles during placement preview.
            var tile = Main.tile[i, j];
            if (!tile.HasTile || tile.TileType != Type) return;

            var entity = DriveBayEntity.FindEntity(i, j);
            if (entity == null) return;

            var topLeft = TileObjectData.TopLeft(i, j);
            // This cell's pixel region within the 48×48 tile.
            int cx = (i - topLeft.X) * 16;
            int cy = (j - topLeft.Y) * 16;

            Vector2 zero = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange, Main.offScreenRange);
            Vector2 cellScreen = new Vector2(i * 16 - (int)Main.screenPosition.X, j * 16 - (int)Main.screenPosition.Y) + zero;

            // --- Drive lights (4 rows × 10 columns) ---
            var states = entity.SlotDisplayState;
            for (int slot = 0; slot < DriveBayEntity.DiskSlotCount; slot++)
            {
                byte s = states[slot];
                if (s == 0) continue;
                int lx = 6 + (slot % 10) * 4;
                int ly = 6 + (slot / 10) * 10;
                if (TryClipToCell(lx, ly, DLW, DLH, cx, cy, out var src, out var off))
                    spriteBatch.Draw(_driveLight[s - 1].Value, cellScreen + off, src, Color.White);
            }

            // --- Bay status light ---
            byte bayIndex;
            if (!entity.IsConnected)
                bayIndex = 0;
            else if (entity.TotalMaxStacks == 0)
                bayIndex = 1;
            else
            {
                float bayPct = (float)entity.TotalUsedStacks / entity.TotalMaxStacks;
                bayIndex = bayPct >= 1f ? (byte)3 : bayPct >= 0.8f ? (byte)2 : (byte)1;
            }
            if (TryClipToCell(BLX, BLY, BLW, BLH, cx, cy, out var bSrc, out var bOff))
                spriteBatch.Draw(_bayLight[bayIndex].Value, cellScreen + bOff, bSrc, Color.White);
        }

        // Clips a tile-local rectangle (lx,ly,w,h) to a cell's 16×16 region (cx,cy).
        // Returns false if there is no overlap; otherwise outputs the source rect and screen offset.
        private static bool TryClipToCell(int lx, int ly, int w, int h, int cx, int cy,
                                          out Rectangle src, out Vector2 offset)
        {
            int ox = Math.Max(lx, cx);
            int oy = Math.Max(ly, cy);
            int or2 = Math.Min(lx + w, cx + 16);
            int ob = Math.Min(ly + h, cy + 16);
            if (ox >= or2 || oy >= ob) { src = default; offset = default; return false; }
            src = new Rectangle(ox - lx, oy - ly, or2 - ox, ob - oy);
            offset = new Vector2(ox - cx, oy - cy);
            return true;
        }
    }
}

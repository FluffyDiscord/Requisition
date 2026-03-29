using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;
using TerraStorage.Content.Items;
using TerraStorage.Content.UI;
using TerraStorage.Helpers;
using TerraStorage.Systems;

namespace TerraStorage.Content.Tiles
{
    // Tile entity attached to each placed Drive Bay. Manages the array of inserted Storage Disks,
    // handles multiplayer synchronization, and persists disk items across save/load cycles.
    public class DriveBayEntity : ModTileEntity
    {
        //Maximum number of Storage Disk slots in a single Drive Bay.
        public const int DiskSlotCount = 40;

        public Item[] DiskSlots { get; private set; } = new Item[DiskSlotCount];

        public override void OnNetPlace()
        {
            InitializeSlots();
        }

        public override bool IsTileValidForEntity(int x, int y)
        {
            // x, y is the entity's stored position (top-left of the multi-tile).
            var tile = Main.tile[x, y];
            return tile.HasTile && tile.TileType == ModContent.TileType<DriveBay>();
        }

        public override int Hook_AfterPlacement(int i, int j, int type, int style, int direction, int alternate)
        {
            // With processedCoordinates: true, i/j is the top-left corner of the multi-tile.
            if (Main.netMode == Terraria.ID.NetmodeID.MultiplayerClient)
            {
                // On a client we can't place tile entities directly; send the tile data and a
                // TileEntityPlacement message so the server creates and replicates the entity.
                NetMessage.SendTileSquare(Main.myPlayer, i, j, 2, 2);
                NetMessage.SendData(Terraria.ID.MessageID.TileEntityPlacement, number: i, number2: j, number3: Type);
                return -1;
            }

            int placedEntity = Place(i, j);
            if (TileEntity.ByID.TryGetValue(placedEntity, out var entity) && entity is DriveBayEntity sbe)
                sbe.InitializeSlots();

            return placedEntity;
        }

        public void EnsureSlotsInitialized() => InitializeSlots();

        //Returns true if at least one disk slot is occupied.
        public bool HasDisks()
        {
            for (int i = 0; i < DiskSlotCount; i++)
                if (DiskSlots[i] != null && !DiskSlots[i].IsAir)
                    return true;
            return false;
        }

        // Ensures every slot contains a non-null, air Item rather than a null reference.
        // Called defensively before any slot access to guard against partial deserialization.
        private void InitializeSlots()
        {
            for (int i = 0; i < DiskSlotCount; i++)
            {
                if (DiskSlots[i] == null)
                {
                    DiskSlots[i] = new Item();
                    DiskSlots[i].TurnToAir();
                }
            }
        }

        // Get all disk IDs from inserted disks.
        public List<Guid> GetInsertedDiskIds()
        {
            var seen = new HashSet<Guid>();
            var ids = new List<Guid>();
            for (int i = 0; i < DiskSlotCount; i++)
            {
                if (DiskSlots[i] != null && !DiskSlots[i].IsAir &&
                    DiskSlots[i].ModItem is StorageDiskBase disk)
                {
                    // Defensive fallback: InsertDisk should have assigned a GUID, but guard
                    // against edge cases (e.g. disks loaded from a pre-fix save file).
                    if (disk.DiskId == Guid.Empty)
                        disk.DiskId = Guid.NewGuid();

                    // Re-register in case the DiskData entry was purged on world load
                    // (empty entries are cleaned up at load time to keep the world tidy).
                    // Only register on the server/singleplayer — clients receive DiskData via network sync.
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        StorageWorldSystem.Instance?.RegisterDisk(disk.DiskId, disk.Tier);
                        // Always sync the tier from the disk item — the item type is the authoritative
                        // source of truth. DiskData.Tier can become stale when the world is reloaded
                        // without saving after a disk upgrade (player file keeps new tier, world save
                        // has old DiskData tier, and GetOrCreateDiskData never overwrites existing entries).
                        StorageWorldSystem.Instance?.UpgradeDisk(disk.DiskId, disk.Tier);
                    }

                    if (seen.Add(disk.DiskId))
                        ids.Add(disk.DiskId);
                }
            }
            return ids;
        }

        // Try to insert a disk into the first available slot.
        // Returns true if successful.
        public bool InsertDisk(Item diskItem, int slot = -1)
        {
            if (diskItem == null || diskItem.IsAir || diskItem.ModItem is not StorageDiskBase disk)
                return false;

            // Archived disks cannot be inserted — they must be unarchived first (middle-click).
            if (disk.IsArchived)
                return false;

            InitializeSlots();

            // Assign a GUID the first time a disk enters a Drive Bay. This is the canonical
            // point at which a disk becomes a storage identity in StorageWorldSystem.
            if (disk.DiskId == Guid.Empty)
            {
                disk.DiskId = Guid.NewGuid();

                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    if (disk.ArchivedItems.Count > 0)
                    {
                        // Restore items from an unarchived disk into this world's storage.
                        StorageWorldSystem.Instance?.RegisterDiskWithItems(disk.DiskId, disk.Tier, disk.ArchivedItems);
                        disk.ArchivedItems.Clear();
                    }
                    else
                    {
                        StorageWorldSystem.Instance?.RegisterDisk(disk.DiskId, disk.Tier);
                    }
                }
                // MP client: GUID assigned but ArchivedItems left intact so they are
                // serialized in the SyncDiskInsert packet. The server handles restoration.
            }
            else if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                // Disk already has a GUID — either a normal re-insert, a world-load, or
                // a client-assigned GUID arriving via SyncDiskInsert with ArchivedItems.
                if (disk.ArchivedItems.Count > 0)
                {
                    StorageWorldSystem.Instance?.RegisterDiskWithItems(disk.DiskId, disk.Tier, disk.ArchivedItems);
                    disk.ArchivedItems.Clear();
                }
                else
                {
                    StorageWorldSystem.Instance?.RegisterDisk(disk.DiskId, disk.Tier);
                    StorageWorldSystem.Instance?.UpgradeDisk(disk.DiskId, disk.Tier);
                }
            }

            if (slot >= 0 && slot < DiskSlotCount)
            {
                if (DiskSlots[slot].IsAir)
                {
                    DiskSlots[slot] = diskItem.Clone();
                    return true;
                }
                return false;
            }

            for (int i = 0; i < DiskSlotCount; i++)
            {
                if (DiskSlots[i].IsAir)
                {
                    DiskSlots[i] = diskItem.Clone();
                    return true;
                }
            }

            return false;
        }

        // Remove a disk from a specific slot. Returns the removed disk item.
        public Item RemoveDisk(int slot)
        {
            InitializeSlots();

            if (slot < 0 || slot >= DiskSlotCount || DiskSlots[slot].IsAir)
                return new Item();

            var removed = DiskSlots[slot].Clone();
            DiskSlots[slot].TurnToAir();
            return removed;
        }

        // Drop all disks when the block is destroyed. 
        public void DropDisks(int x, int y)
        {
            InitializeSlots();
            for (int i = 0; i < DiskSlotCount; i++)
            {
                if (!DiskSlots[i].IsAir)
                {
                    int idx = Item.NewItem(new EntitySource_TileBreak(x, y), x * 16, y * 16, 32, 32, DiskSlots[i].type, DiskSlots[i].stack, false, DiskSlots[i].prefix);
                    if (Main.netMode != Terraria.ID.NetmodeID.MultiplayerClient && DiskSlots[i].ModItem != null)
                        Main.item[idx] = DiskSlots[i].Clone();
                    DiskSlots[i].TurnToAir();
                }
            }
        }

        // Open the disk insertion UI for this storage block. 
        public void OpenDiskUI(Player player)
        {
            var uiSystem = ModContent.GetInstance<DriveBayUISystem>();
            uiSystem?.OpenDriveBay(this);
        }

        public override void SaveData(TagCompound tag)
        {
            InitializeSlots();
            var diskTags = new List<TagCompound>();
            for (int i = 0; i < DiskSlotCount; i++)
            {
                diskTags.Add(ItemIO.Save(DiskSlots[i]));
            }
            tag["disks"] = diskTags;
        }

        public override void LoadData(TagCompound tag)
        {
            InitializeSlots();
            if (tag.ContainsKey("disks"))
            {
                var diskTags = tag.GetList<TagCompound>("disks");
                for (int i = 0; i < DiskSlotCount && i < diskTags.Count; i++)
                {
                    DiskSlots[i] = ItemIO.Load(diskTags[i]);
                }
            }
        }

        //Serializes all disk slots over the network (server → clients).
        public override void NetSend(BinaryWriter writer)
        {
            InitializeSlots();

            // Assign GUIDs to uninitialized disks before broadcasting so clients receive
            // GUID-bearing items and can request disk data with the correct IDs.
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int i = 0; i < DiskSlotCount; i++)
                {
                    if (DiskSlots[i] != null && !DiskSlots[i].IsAir &&
                        DiskSlots[i].ModItem is StorageDiskBase disk && disk.DiskId == Guid.Empty)
                    {
                        disk.DiskId = Guid.NewGuid();
                        StorageWorldSystem.Instance?.RegisterDisk(disk.DiskId, disk.Tier);
                    }
                }
            }

            for (int i = 0; i < DiskSlotCount; i++)
            {
                // StorageDiskBase.NetSend writes the GUID so it is preserved across the wire.
                ItemIO.Send(DiskSlots[i], writer, true);
            }
        }

        //Deserializes all disk slots received from the network.
        public override void NetReceive(BinaryReader reader)
        {
            InitializeSlots();
            for (int i = 0; i < DiskSlotCount; i++)
            {
                DiskSlots[i] = ItemIO.Receive(reader, true);
            }
        }

        // Find the DriveBayEntity at a given tile position (accounts for multi-tile).
        public static DriveBayEntity FindEntity(int i, int j)
        {
            var tile = Main.tile[i, j];
            if (!tile.HasTile)
                return null;

            // Entity is stored at the top-left corner of the multi-tile.
            Point16 topLeft = TileObjectData.TopLeft(i, j);
            if (topLeft == Point16.NegativeOne)
                return null;

            if (TileEntity.ByPosition.TryGetValue(topLeft, out var entity) && entity is DriveBayEntity sbe)
                return sbe;

            return null;
        }
    }
}
